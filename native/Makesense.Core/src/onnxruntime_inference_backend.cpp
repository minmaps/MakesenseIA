#include "inference_backend.h"

#include <onnxruntime_cxx_api.h>

#include <windows.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr std::size_t max_detection_results = 3;
    constexpr std::size_t max_pose_results = 5;
    constexpr float detection_score_threshold = 0.25f;
    constexpr float pose_score_threshold = 0.20f;
    constexpr float nms_iou_threshold = 0.45f;
    constexpr float letterbox_fill_value = 114.0f / 255.0f;

    struct session_entry
    {
        std::wstring model_path;
        std::wstring provider_name;
        std::uintmax_t model_size{0};
        std::filesystem::file_time_type last_write_time{};
        std::unique_ptr<Ort::Session> session;
        std::string input_name;
        std::vector<std::string> output_names;
        std::vector<const char*> output_name_ptrs;
        std::vector<int64_t> input_shape;
        bool nchw{true};
        std::int64_t input_width{640};
        std::int64_t input_height{640};
    };

    struct preprocessed_image
    {
        std::vector<float> tensor;
        std::vector<int64_t> input_shape;
        std::uint32_t source_width{0};
        std::uint32_t source_height{0};
        float scale{1.0f};
        float pad_x{0.0f};
        float pad_y{0.0f};
        bool nchw{true};
        std::int64_t input_width{640};
        std::int64_t input_height{640};
    };

    struct tensor_view
    {
        const float* data{nullptr};
        std::vector<int64_t> shape;
        std::size_t element_count{0};
    };

    std::wstring widen_utf8(const std::string& value)
    {
        if (value.empty())
        {
            return {};
        }

        const auto required_size = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0);
        std::wstring result(required_size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), result.data(), required_size);
        return result;
    }

    std::string narrow_utf8(const std::wstring& value)
    {
        if (value.empty())
        {
            return {};
        }

        const auto required_size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        std::string result(required_size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), result.data(), required_size, nullptr, nullptr);
        return result;
    }

    std::wstring task_to_base_label(const std::wstring& task_name, const std::wstring& model_path)
    {
        const auto model_name = std::filesystem::path(model_path).stem().wstring();
        if (!model_name.empty())
        {
            return model_name;
        }

        return task_name == L"pose-estimation" ? L"keypoint" : L"object";
    }

    bool resolve_input_layout(
        const std::vector<int64_t>& raw_shape,
        bool& nchw,
        std::vector<int64_t>& concrete_shape,
        std::int64_t& input_width,
        std::int64_t& input_height)
    {
        if (raw_shape.size() != 4)
        {
            return false;
        }

        nchw = true;
        if (raw_shape[3] == 3 || raw_shape[3] == 1)
        {
            nchw = false;
        }
        else if (raw_shape[1] == 3 || raw_shape[1] == 1)
        {
            nchw = true;
        }

        input_height = nchw ? raw_shape[2] : raw_shape[1];
        input_width = nchw ? raw_shape[3] : raw_shape[2];
        if (input_height <= 0)
        {
            input_height = 640;
        }
        if (input_width <= 0)
        {
            input_width = 640;
        }

        concrete_shape = raw_shape;
        concrete_shape[0] = 1;
        if (nchw)
        {
            concrete_shape[1] = 3;
            concrete_shape[2] = input_height;
            concrete_shape[3] = input_width;
        }
        else
        {
            concrete_shape[1] = input_height;
            concrete_shape[2] = input_width;
            concrete_shape[3] = 3;
        }

        return true;
    }

    std::optional<tensor_view> make_tensor_view(Ort::Value& value)
    {
        if (!value.IsTensor())
        {
            return std::nullopt;
        }

        auto tensor_info = value.GetTensorTypeAndShapeInfo();
        if (tensor_info.GetElementType() != ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT)
        {
            return std::nullopt;
        }

        tensor_view view;
        view.shape = tensor_info.GetShape();
        view.element_count = tensor_info.GetElementCount();
        view.data = value.GetTensorData<float>();
        return view;
    }

    bool create_wic_factory(ComPtr<IWICImagingFactory>& factory)
    {
        return SUCCEEDED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory)));
    }

    bool load_and_preprocess_image(
        const std::wstring& image_path,
        const session_entry& session,
        preprocessed_image& output,
        std::wstring& status)
    {
        ComPtr<IWICImagingFactory> wic_factory;
        if (!create_wic_factory(wic_factory))
        {
            status = L"Failed to create WIC factory for inference preprocessing.";
            return false;
        }

        ComPtr<IWICBitmapDecoder> decoder;
        if (FAILED(wic_factory->CreateDecoderFromFilename(image_path.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &decoder)))
        {
            status = L"Failed to decode image for inference preprocessing.";
            return false;
        }

        ComPtr<IWICBitmapFrameDecode> frame;
        if (FAILED(decoder->GetFrame(0, &frame)))
        {
            status = L"Failed to access the first image frame for inference.";
            return false;
        }

        UINT source_width = 0;
        UINT source_height = 0;
        if (FAILED(frame->GetSize(&source_width, &source_height)) || source_width == 0 || source_height == 0)
        {
            status = L"Image dimensions are invalid for inference.";
            return false;
        }

        ComPtr<IWICFormatConverter> converter;
        if (FAILED(wic_factory->CreateFormatConverter(&converter)) ||
            FAILED(converter->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nullptr, 0.0f, WICBitmapPaletteTypeMedianCut)))
        {
            status = L"Failed to convert the input image into a GPU-ready format.";
            return false;
        }

        const auto scale = std::min(
            static_cast<float>(session.input_width) / static_cast<float>(source_width),
            static_cast<float>(session.input_height) / static_cast<float>(source_height));
        const auto resized_width = std::max<UINT>(1, static_cast<UINT>(std::round(static_cast<float>(source_width) * scale)));
        const auto resized_height = std::max<UINT>(1, static_cast<UINT>(std::round(static_cast<float>(source_height) * scale)));
        const auto pad_x = (static_cast<float>(session.input_width) - static_cast<float>(resized_width)) * 0.5f;
        const auto pad_y = (static_cast<float>(session.input_height) - static_cast<float>(resized_height)) * 0.5f;

        ComPtr<IWICBitmapSource> source_bitmap = converter;
        ComPtr<IWICBitmapScaler> scaler;
        if (resized_width != source_width || resized_height != source_height)
        {
            if (FAILED(wic_factory->CreateBitmapScaler(&scaler)) ||
                FAILED(scaler->Initialize(converter.Get(), resized_width, resized_height, WICBitmapInterpolationModeFant)))
            {
                status = L"Failed to resize the input image for model inference.";
                return false;
            }

            source_bitmap = scaler;
        }

        std::vector<std::uint8_t> resized_pixels(static_cast<std::size_t>(resized_width) * resized_height * 4ULL);
        const auto stride = resized_width * 4U;
        if (FAILED(source_bitmap->CopyPixels(nullptr, stride, static_cast<UINT>(resized_pixels.size()), resized_pixels.data())))
        {
            status = L"Failed to read resized image pixels for inference.";
            return false;
        }

        output.source_width = source_width;
        output.source_height = source_height;
        output.scale = scale;
        output.pad_x = pad_x;
        output.pad_y = pad_y;
        output.nchw = session.nchw;
        output.input_width = session.input_width;
        output.input_height = session.input_height;
        output.input_shape = session.input_shape;
        output.tensor.assign(static_cast<std::size_t>(session.input_width) * session.input_height * 3ULL, letterbox_fill_value);

        const auto target_width = static_cast<std::size_t>(session.input_width);
        const auto target_height = static_cast<std::size_t>(session.input_height);
        const auto pad_left = static_cast<std::size_t>(std::floor(pad_x));
        const auto pad_top = static_cast<std::size_t>(std::floor(pad_y));

        auto write_pixel = [&](std::size_t x, std::size_t y, float red, float green, float blue)
        {
            if (output.nchw)
            {
                const auto plane = target_width * target_height;
                const auto index = y * target_width + x;
                output.tensor[index] = red;
                output.tensor[plane + index] = green;
                output.tensor[(2 * plane) + index] = blue;
            }
            else
            {
                const auto index = (y * target_width + x) * 3ULL;
                output.tensor[index] = red;
                output.tensor[index + 1ULL] = green;
                output.tensor[index + 2ULL] = blue;
            }
        };

        for (std::size_t y = 0; y < resized_height; ++y)
        {
            for (std::size_t x = 0; x < resized_width; ++x)
            {
                const auto source_index = (y * resized_width + x) * 4ULL;
                const auto blue = resized_pixels[source_index] / 255.0f;
                const auto green = resized_pixels[source_index + 1ULL] / 255.0f;
                const auto red = resized_pixels[source_index + 2ULL] / 255.0f;
                const auto target_x = std::min<std::size_t>(target_width - 1ULL, pad_left + x);
                const auto target_y = std::min<std::size_t>(target_height - 1ULL, pad_top + y);
                write_pixel(target_x, target_y, red, green, blue);
            }
        }

        return true;
    }

    float compute_iou(const native_inference_suggestion& left, const native_inference_suggestion& right)
    {
        const auto left_x2 = left.rect_x + left.rect_width;
        const auto left_y2 = left.rect_y + left.rect_height;
        const auto right_x2 = right.rect_x + right.rect_width;
        const auto right_y2 = right.rect_y + right.rect_height;

        const auto intersection_x1 = std::max(left.rect_x, right.rect_x);
        const auto intersection_y1 = std::max(left.rect_y, right.rect_y);
        const auto intersection_x2 = std::min(left_x2, right_x2);
        const auto intersection_y2 = std::min(left_y2, right_y2);
        const auto intersection_width = std::max(0.0f, intersection_x2 - intersection_x1);
        const auto intersection_height = std::max(0.0f, intersection_y2 - intersection_y1);
        const auto intersection_area = intersection_width * intersection_height;
        const auto union_area = left.rect_width * left.rect_height + right.rect_width * right.rect_height - intersection_area;
        return union_area > 0.0f ? intersection_area / union_area : 0.0f;
    }

    void reverse_letterbox(
        float& x1,
        float& y1,
        float& x2,
        float& y2,
        const preprocessed_image& preprocessed)
    {
        x1 = (x1 - preprocessed.pad_x) / preprocessed.scale;
        y1 = (y1 - preprocessed.pad_y) / preprocessed.scale;
        x2 = (x2 - preprocessed.pad_x) / preprocessed.scale;
        y2 = (y2 - preprocessed.pad_y) / preprocessed.scale;

        x1 = std::clamp(x1, 0.0f, static_cast<float>(preprocessed.source_width));
        y1 = std::clamp(y1, 0.0f, static_cast<float>(preprocessed.source_height));
        x2 = std::clamp(x2, 0.0f, static_cast<float>(preprocessed.source_width));
        y2 = std::clamp(y2, 0.0f, static_cast<float>(preprocessed.source_height));
    }

    std::vector<native_inference_suggestion> parse_detection_output(
        const tensor_view& tensor,
        const preprocessed_image& preprocessed,
        const std::wstring& base_label)
    {
        if (tensor.shape.size() < 2 || tensor.data == nullptr || tensor.element_count == 0)
        {
            return {};
        }

        std::size_t proposal_count = 0;
        std::size_t feature_count = 0;
        bool feature_major = false;

        if (tensor.shape.size() == 3)
        {
            const auto dimension_a = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[1], 1));
            const auto dimension_b = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[2], 1));
            feature_major = dimension_a <= 128 && dimension_b > dimension_a;
            if (feature_major)
            {
                feature_count = dimension_a;
                proposal_count = dimension_b;
            }
            else
            {
                proposal_count = dimension_a;
                feature_count = dimension_b;
            }
        }
        else
        {
            proposal_count = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[tensor.shape.size() - 2], 1));
            feature_count = static_cast<std::size_t>(std::max<int64_t>(tensor.shape.back(), 1));
        }

        if (proposal_count == 0 || feature_count < 5)
        {
            return {};
        }

        const auto get_value = [&](std::size_t proposal, std::size_t feature) -> float
        {
            return feature_major
                ? tensor.data[feature * proposal_count + proposal]
                : tensor.data[proposal * feature_count + feature];
        };

        std::vector<native_inference_suggestion> candidates;
        candidates.reserve(proposal_count);

        for (std::size_t proposal = 0; proposal < proposal_count; ++proposal)
        {
            float score = 0.0f;
            std::size_t class_index = 0;

            if (feature_count == 6)
            {
                score = get_value(proposal, 4);
                class_index = static_cast<std::size_t>(std::max(0.0f, std::round(get_value(proposal, 5))));
            }
            else
            {
                float best_yolov5 = 0.0f;
                std::size_t best_yolov5_class = 0;
                for (std::size_t feature = 5; feature < feature_count; ++feature)
                {
                    const auto class_score = get_value(proposal, feature);
                    if (class_score > best_yolov5)
                    {
                        best_yolov5 = class_score;
                        best_yolov5_class = feature - 5;
                    }
                }

                float best_yolov8 = 0.0f;
                std::size_t best_yolov8_class = 0;
                for (std::size_t feature = 4; feature < feature_count; ++feature)
                {
                    const auto class_score = get_value(proposal, feature);
                    if (class_score > best_yolov8)
                    {
                        best_yolov8 = class_score;
                        best_yolov8_class = feature - 4;
                    }
                }

                const auto objectness = std::clamp(get_value(proposal, 4), 0.0f, 1.0f);
                const auto yolov5_score = objectness * best_yolov5;
                if (yolov5_score >= best_yolov8)
                {
                    score = yolov5_score;
                    class_index = best_yolov5_class;
                }
                else
                {
                    score = best_yolov8;
                    class_index = best_yolov8_class;
                }
            }

            if (score < detection_score_threshold)
            {
                continue;
            }

            auto x1 = get_value(proposal, 0);
            auto y1 = get_value(proposal, 1);
            auto x2 = get_value(proposal, 2);
            auto y2 = get_value(proposal, 3);
            const auto probably_xyxy =
                x2 > x1 &&
                y2 > y1 &&
                x2 <= static_cast<float>(preprocessed.input_width) * 1.1f &&
                y2 <= static_cast<float>(preprocessed.input_height) * 1.1f;

            if (!probably_xyxy)
            {
                const auto width = x2;
                const auto height = y2;
                x1 = x1 - width * 0.5f;
                y1 = y1 - height * 0.5f;
                x2 = x1 + width;
                y2 = y1 + height;
            }

            reverse_letterbox(x1, y1, x2, y2, preprocessed);
            const auto width = std::max(0.0f, x2 - x1);
            const auto height = std::max(0.0f, y2 - y1);
            if (width < 2.0f || height < 2.0f)
            {
                continue;
            }

            native_inference_suggestion suggestion;
            suggestion.kind = MS_INFERENCE_SUGGESTION_RECT;
            suggestion.confidence = score;
            suggestion.id = L"det-" + std::to_wstring(proposal);
            suggestion.label_name = base_label + L"-" + std::to_wstring(class_index);
            suggestion.suggested_label = suggestion.label_name;
            suggestion.rect_x = x1;
            suggestion.rect_y = y1;
            suggestion.rect_width = width;
            suggestion.rect_height = height;
            candidates.push_back(std::move(suggestion));
        }

        std::sort(candidates.begin(), candidates.end(), [](const auto& left, const auto& right)
        {
            return left.confidence > right.confidence;
        });

        std::vector<native_inference_suggestion> filtered;
        filtered.reserve(std::min<std::size_t>(candidates.size(), max_detection_results));
        for (const auto& candidate : candidates)
        {
            const auto suppressed = std::any_of(filtered.begin(), filtered.end(), [&](const auto& kept)
            {
                return compute_iou(candidate, kept) > nms_iou_threshold;
            });

            if (!suppressed)
            {
                filtered.push_back(candidate);
            }

            if (filtered.size() >= max_detection_results)
            {
                break;
            }
        }

        return filtered;
    }

    std::vector<native_inference_suggestion> parse_pose_output(
        const tensor_view& tensor,
        const preprocessed_image& preprocessed,
        const std::wstring& base_label)
    {
        std::vector<native_inference_suggestion> suggestions;
        if (tensor.data == nullptr || tensor.element_count == 0 || tensor.shape.size() < 2)
        {
            return suggestions;
        }

        std::size_t proposal_count = 0;
        std::size_t feature_count = 0;
        bool feature_major = false;
        if (tensor.shape.size() == 3)
        {
            const auto dimension_a = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[1], 1));
            const auto dimension_b = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[2], 1));
            feature_major = dimension_a <= 128 && dimension_b > dimension_a;
            if (feature_major)
            {
                feature_count = dimension_a;
                proposal_count = dimension_b;
            }
            else
            {
                proposal_count = dimension_a;
                feature_count = dimension_b;
            }
        }
        else
        {
            proposal_count = static_cast<std::size_t>(std::max<int64_t>(tensor.shape[tensor.shape.size() - 2], 1));
            feature_count = static_cast<std::size_t>(std::max<int64_t>(tensor.shape.back(), 1));
        }

        if (proposal_count == 0)
        {
            return suggestions;
        }

        const auto get_value = [&](std::size_t proposal, std::size_t feature) -> float
        {
            return feature_major
                ? tensor.data[feature * proposal_count + proposal]
                : tensor.data[proposal * feature_count + feature];
        };

        std::size_t best_proposal = 0;
        float best_score = -1.0f;
        for (std::size_t proposal = 0; proposal < proposal_count; ++proposal)
        {
            const auto score = feature_count >= 5 ? get_value(proposal, 4) : 1.0f;
            if (score > best_score)
            {
                best_score = score;
                best_proposal = proposal;
            }
        }

        const auto offset = (feature_count >= 5 && (feature_count - 5) % 3 == 0)
            ? 5ULL
            : ((feature_count >= 4 && (feature_count - 4) % 3 == 0) ? 4ULL : 0ULL);
        if (offset == 0 || (feature_count - offset) < 3)
        {
            return suggestions;
        }

        const auto keypoint_count = (feature_count - offset) / 3ULL;
        for (std::size_t keypoint = 0; keypoint < keypoint_count; ++keypoint)
        {
            const auto x = get_value(best_proposal, offset + keypoint * 3ULL);
            const auto y = get_value(best_proposal, offset + keypoint * 3ULL + 1ULL);
            const auto confidence = get_value(best_proposal, offset + keypoint * 3ULL + 2ULL);
            if (confidence < pose_score_threshold)
            {
                continue;
            }

            auto resolved_x = (x - preprocessed.pad_x) / preprocessed.scale;
            auto resolved_y = (y - preprocessed.pad_y) / preprocessed.scale;
            resolved_x = std::clamp(resolved_x, 0.0f, static_cast<float>(preprocessed.source_width));
            resolved_y = std::clamp(resolved_y, 0.0f, static_cast<float>(preprocessed.source_height));

            native_inference_suggestion suggestion;
            suggestion.kind = MS_INFERENCE_SUGGESTION_POINT;
            suggestion.confidence = confidence;
            suggestion.id = L"pose-" + std::to_wstring(keypoint);
            suggestion.label_name = base_label;
            suggestion.suggested_label = base_label;
            suggestion.point_x = resolved_x;
            suggestion.point_y = resolved_y;
            suggestions.push_back(std::move(suggestion));

            if (suggestions.size() >= max_pose_results)
            {
                break;
            }
        }

        std::sort(suggestions.begin(), suggestions.end(), [](const auto& left, const auto& right)
        {
            return left.confidence > right.confidence;
        });
        return suggestions;
    }

    class onnxruntime_inference_backend final : public inference_backend
    {
    public:
        onnxruntime_inference_backend()
            : env_(ORT_LOGGING_LEVEL_WARNING, "Makesense.Native.Core")
        {
        }

        bool available() const override
        {
            return true;
        }

        std::wstring backend_summary() const override
        {
            return L"ONNX Runtime + TensorRT/CUDA";
        }

        ms_result_code run(const inference_run_request& request, inference_run_result& result, std::wstring& status) override
        {
            auto session = get_or_create_session(request, status);
            if (!session)
            {
                return MS_RESULT_NOT_SUPPORTED;
            }

            preprocessed_image preprocessed;
            if (!load_and_preprocess_image(request.image_path, *session, preprocessed, status))
            {
                return MS_RESULT_ERROR;
            }

            Ort::MemoryInfo memory_info = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
            auto input_tensor = Ort::Value::CreateTensor<float>(
                memory_info,
                preprocessed.tensor.data(),
                preprocessed.tensor.size(),
                preprocessed.input_shape.data(),
                preprocessed.input_shape.size());

            std::array<const char*, 1> input_names = { session->input_name.c_str() };
            std::vector<Ort::Value> outputs;
            try
            {
                outputs = session->session->Run(
                    Ort::RunOptions{nullptr},
                    input_names.data(),
                    &input_tensor,
                    input_names.size(),
                    session->output_name_ptrs.data(),
                    session->output_name_ptrs.size());
            }
            catch (const Ort::Exception& exception)
            {
                status = L"ONNX Runtime execution failed: " + widen_utf8(exception.what());
                return MS_RESULT_ERROR;
            }

            std::vector<native_inference_suggestion> suggestions;
            const auto base_label = task_to_base_label(request.task_name, request.model_path);
            for (auto& output : outputs)
            {
                const auto tensor = make_tensor_view(output);
                if (!tensor.has_value())
                {
                    continue;
                }

                suggestions = request.task_name == L"pose-estimation"
                    ? parse_pose_output(*tensor, preprocessed, base_label)
                    : parse_detection_output(*tensor, preprocessed, base_label);

                if (!suggestions.empty())
                {
                    break;
                }
            }

            result.backend_name = L"ONNX Runtime";
            result.provider_name = session->provider_name;
            result.suggestions = std::move(suggestions);
            status = result.suggestions.empty()
                ? L"ONNX Runtime completed on " + session->provider_name + L" but produced no supported suggestions."
                : L"Inference results ready for " + request.task_name + L" using " + session->provider_name + L".";
            return MS_RESULT_OK;
        }

    private:
        std::shared_ptr<session_entry> get_or_create_session(const inference_run_request& request, std::wstring& status)
        {
            std::error_code error;
            const auto model_size = std::filesystem::file_size(request.model_path, error);
            if (error)
            {
                status = L"Failed to inspect the ONNX model before inference.";
                return nullptr;
            }

            const auto last_write_time = std::filesystem::last_write_time(request.model_path, error);
            if (error)
            {
                status = L"Failed to read the ONNX model timestamp.";
                return nullptr;
            }

            const auto cache_key = request.model_path;
            {
                std::scoped_lock lock(cache_mutex_);
                const auto existing = session_cache_.find(cache_key);
                if (existing != session_cache_.end() &&
                    existing->second->model_size == model_size &&
                    existing->second->last_write_time == last_write_time)
                {
                    return existing->second;
                }
            }

            auto created = create_session(request, model_size, last_write_time, status);
            if (!created)
            {
                return nullptr;
            }

            std::scoped_lock lock(cache_mutex_);
            session_cache_[cache_key] = created;
            return created;
        }

        std::shared_ptr<session_entry> create_session(
            const inference_run_request& request,
            std::uintmax_t model_size,
            std::filesystem::file_time_type last_write_time,
            std::wstring& status)
        {
            const std::array<std::pair<std::wstring, bool>, 2> provider_attempts =
            {{
                {L"TensorRT", true},
                {L"CUDA", false}
            }};

            for (const auto& [provider_name, use_tensorrt] : provider_attempts)
            {
                try
                {
                    Ort::SessionOptions options;
                    options.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);
                    options.SetExecutionMode(ExecutionMode::ORT_SEQUENTIAL);
                    options.SetIntraOpNumThreads(1);

                    if (use_tensorrt)
                    {
                        std::filesystem::create_directories(request.cache_directory);
                        auto cache_path = narrow_utf8(request.cache_directory);
                        OrtTensorRTProviderOptions trt_options{};
                        trt_options.device_id = 0;
                        trt_options.trt_fp16_enable = 1;
                        trt_options.trt_engine_cache_enable = 1;
                        trt_options.trt_engine_cache_path = cache_path.c_str();
                        trt_options.trt_max_partition_iterations = 1000;
                        trt_options.trt_min_subgraph_size = 1;
                        options.AppendExecutionProvider_TensorRT(trt_options);
                    }
                    else
                    {
                        OrtCUDAProviderOptions cuda_options{};
                        cuda_options.device_id = 0;
                        options.AppendExecutionProvider_CUDA(cuda_options);
                    }

                    auto session = std::make_unique<Ort::Session>(env_, request.model_path.c_str(), options);
                    auto entry = std::make_shared<session_entry>();
                    entry->model_path = request.model_path;
                    entry->provider_name = provider_name;
                    entry->model_size = model_size;
                    entry->last_write_time = last_write_time;
                    entry->session = std::move(session);

                    Ort::AllocatorWithDefaultOptions allocator;
                    auto input_name = entry->session->GetInputNameAllocated(0, allocator);
                    entry->input_name = input_name.get();

                    const auto input_info = entry->session->GetInputTypeInfo(0).GetTensorTypeAndShapeInfo();
                    if (!resolve_input_layout(input_info.GetShape(), entry->nchw, entry->input_shape, entry->input_width, entry->input_height))
                    {
                        status = L"Model input layout is unsupported. Expected a 4D image tensor.";
                        return nullptr;
                    }

                    const auto output_count = entry->session->GetOutputCount();
                    entry->output_names.reserve(output_count);
                    entry->output_name_ptrs.reserve(output_count);
                    for (std::size_t index = 0; index < output_count; ++index)
                    {
                        auto output_name = entry->session->GetOutputNameAllocated(index, allocator);
                        entry->output_names.emplace_back(output_name.get());
                    }

                    for (const auto& output_name : entry->output_names)
                    {
                        entry->output_name_ptrs.push_back(output_name.c_str());
                    }

                    return entry;
                }
                catch (const Ort::Exception& exception)
                {
                    status = L"Failed to initialize " + provider_name + L" provider: " + widen_utf8(exception.what());
                }
            }

            return nullptr;
        }

        Ort::Env env_;
        std::mutex cache_mutex_;
        std::unordered_map<std::wstring, std::shared_ptr<session_entry>> session_cache_;
    };
}

std::unique_ptr<inference_backend> create_onnxruntime_inference_backend()
{
    return std::make_unique<onnxruntime_inference_backend>();
}

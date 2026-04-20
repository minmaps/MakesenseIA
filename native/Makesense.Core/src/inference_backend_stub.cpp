#include "inference_backend.h"

#include <algorithm>
#include <filesystem>
#include <random>

namespace
{
    constexpr std::uint32_t max_inference_suggestion_count = 3;

    std::wstring task_to_label_name(const std::wstring& task_name, const std::wstring& model_path)
    {
        const auto model_name = std::filesystem::path(model_path).stem().wstring();
        if (!model_name.empty())
        {
            return model_name;
        }

        if (task_name == L"pose-estimation")
        {
            return L"keypoint";
        }

        if (task_name == L"rect-detection")
        {
            return L"object";
        }

        return task_name.empty() ? L"object" : task_name;
    }

    class synthetic_inference_backend final : public inference_backend
    {
    public:
        bool available() const override
        {
            return true;
        }

        std::wstring backend_summary() const override
        {
            return L"Synthetic fallback";
        }

        ms_result_code run(const inference_run_request& request, inference_run_result& result, std::wstring& status) override
        {
            result.backend_name = L"Synthetic fallback";
            result.provider_name = L"CPU stub";
            result.suggestions = build_synthetic_inference_suggestions(
                request.image_path,
                request.model_path,
                request.task_name,
                request.image_size);
            status = L"Inference completed with the synthetic fallback backend because ONNX Runtime TensorRT/CUDA is unavailable.";
            return MS_RESULT_OK;
        }
    };
}

std::vector<native_inference_suggestion> build_synthetic_inference_suggestions(
    const std::wstring& image_path,
    const std::wstring& model_path,
    const std::wstring& task_name,
    D2D1_SIZE_F image_size)
{
    const auto width = std::max(1.0f, image_size.width);
    const auto height = std::max(1.0f, image_size.height);
    const auto label_name = task_to_label_name(task_name, model_path);
    const auto seed = static_cast<std::uint32_t>(std::hash<std::wstring>{}(image_path) ^
        (std::hash<std::wstring>{}(model_path) << 1U) ^
        (std::hash<std::wstring>{}(task_name) << 2U));
    std::mt19937 rng(seed);
    std::uniform_real_distribution<float> confidence_distribution(0.62f, 0.96f);

    std::vector<native_inference_suggestion> suggestions;
    suggestions.reserve(max_inference_suggestion_count);

    if (task_name == L"pose-estimation")
    {
        const auto center_x = width * (0.35f + (static_cast<float>(rng() % 1000U) / 1000.0f) * 0.3f);
        const auto center_y = height * (0.25f + (static_cast<float>(rng() % 1000U) / 1000.0f) * 0.35f);
        const auto spread = std::min(width, height) * 0.08f;

        for (std::uint32_t index = 0; index < 5; ++index)
        {
            native_inference_suggestion suggestion;
            const auto offset = static_cast<float>(index) - 2.0f;
            suggestion.kind = MS_INFERENCE_SUGGESTION_POINT;
            suggestion.confidence = confidence_distribution(rng);
            suggestion.id = L"pose-" + std::to_wstring(index);
            suggestion.label_name = label_name;
            suggestion.suggested_label = label_name;
            suggestion.point_x = std::clamp(center_x + offset * spread * 0.55f, 0.0f, width);
            suggestion.point_y = std::clamp(center_y + ((index % 2U == 0U) ? -spread : spread), 0.0f, height);
            suggestions.push_back(std::move(suggestion));
        }

        return suggestions;
    }

    const auto rect_width_upper = std::max(1.0f, width * 0.45f);
    const auto rect_height_upper = std::max(1.0f, height * 0.45f);
    const auto desired_width = std::clamp(width * 0.22f, std::min(48.0f, rect_width_upper), rect_width_upper);
    const auto desired_height = std::clamp(height * 0.18f, std::min(48.0f, rect_height_upper), rect_height_upper);
    const auto max_x = static_cast<std::uint32_t>(std::max(1.0f, width - desired_width));
    const auto max_y = static_cast<std::uint32_t>(std::max(1.0f, height - desired_height));

    for (std::uint32_t index = 0; index < 3; ++index)
    {
        native_inference_suggestion suggestion;
        suggestion.kind = MS_INFERENCE_SUGGESTION_RECT;
        suggestion.confidence = confidence_distribution(rng);
        suggestion.id = L"rect-" + std::to_wstring(index);
        suggestion.label_name = label_name;
        suggestion.suggested_label = label_name;
        suggestion.rect_x = std::clamp(static_cast<float>(rng() % max_x), 0.0f, width - desired_width);
        suggestion.rect_y = std::clamp(static_cast<float>(rng() % max_y), 0.0f, height - desired_height);
        suggestion.rect_width = desired_width;
        suggestion.rect_height = desired_height;
        suggestions.push_back(std::move(suggestion));
    }

    return suggestions;
}

std::unique_ptr<inference_backend> create_inference_backend()
{
#ifdef MS_WITH_ONNXRUNTIME
    std::unique_ptr<inference_backend> create_onnxruntime_inference_backend();
    if (auto backend = create_onnxruntime_inference_backend(); backend && backend->available())
    {
        return backend;
    }
#endif

    return std::make_unique<synthetic_inference_backend>();
}

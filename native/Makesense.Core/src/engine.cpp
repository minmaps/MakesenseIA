#include "engine.h"

#include <algorithm>
#include <chrono>
#include <cwchar>
#include <cstring>
#include <fstream>
#include <sstream>
#include <system_error>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr D2D1_COLOR_F clear_color{0.043f, 0.078f, 0.129f, 1.0f};
    constexpr D2D1_COLOR_F accent_color{0.31f, 0.82f, 0.77f, 1.0f};
    constexpr D2D1_COLOR_F panel_color{0.10f, 0.18f, 0.28f, 0.85f};

    std::size_t to_bytes(std::uint32_t megabytes)
    {
        return static_cast<std::size_t>(megabytes) * 1024ULL * 1024ULL;
    }

    template <std::size_t N>
    void copy_wstring(wchar_t (&destination)[N], const std::wstring& source)
    {
        ::wcsncpy_s(destination, N, source.c_str(), _TRUNCATE);
    }
}

native_engine::native_engine(HWND hwnd, std::uint32_t width, std::uint32_t height, const ms_performance_config& config, bool debug_layer)
    : hwnd_(hwnd), width_(width), height_(height), performance_(config), debug_layer_(debug_layer)
{
    ram_cache_.set_limit(to_bytes(performance_.max_ram_mb));
    vram_cache_.set_limit(to_bytes(performance_.max_vram_mb));
    reconfigure_workers();
    inference_backend_ = create_inference_backend();
}

native_engine::~native_engine()
{
    inference_executor_.shutdown();
    decode_executor_.shutdown();
    io_executor_.shutdown();
}

ms_result_code native_engine::set_performance_limits(const ms_performance_config& config)
{
    std::lock_guard lock(state_mutex_);
    performance_ = config;
    ram_cache_.set_limit(to_bytes(performance_.max_ram_mb));
    vram_cache_.set_limit(to_bytes(performance_.max_vram_mb));
    reconfigure_workers();
    update_status(L"Performance limits updated.");
    return MS_RESULT_OK;
}

ms_result_code native_engine::open_project(const std::wstring& project_path)
{
    std::lock_guard lock(state_mutex_);
    project_path_ = project_path;
    update_status(L"Project manifest registered.");
    return MS_RESULT_OK;
}

ms_result_code native_engine::open_images(const std::wstring& image_paths_blob)
{
    auto paths = split_lines(image_paths_blob);
    {
        std::lock_guard lock(state_mutex_);
        images_ = paths;
        active_image_ = images_.empty() ? L"" : images_.front();
        loaded_image_path_.clear();
        active_image_bitmap_.Reset();
        clear_latest_inference_result_locked();
        update_status(L"Images added to native session.");
    }

    const auto generation = ++image_generation_;
    const auto prefetch_limit = std::min<std::size_t>(paths.size(), performance_.max_prefetch_images);
    for (std::size_t index = 0; index < paths.size(); ++index)
    {
        const auto path = paths[index];

        io_executor_.submit([this, generation, path]
        {
            if (generation != image_generation_.load())
            {
                return;
            }

            std::error_code error;
            const auto file_size = std::filesystem::file_size(path, error);
            if (error)
            {
                return;
            }

            std::scoped_lock lock(state_mutex_);
            ram_cache_.touch(path, static_cast<std::size_t>(file_size) * 2ULL);
        });

        if (index < prefetch_limit)
        {
            decode_executor_.submit([this, generation, path]
            {
                if (generation != image_generation_.load())
                {
                    return;
                }

                std::error_code error;
                const auto file_size = std::filesystem::file_size(path, error);
                if (error)
                {
                    return;
                }

                std::scoped_lock lock(state_mutex_);
                vram_cache_.touch(path, static_cast<std::size_t>(file_size));
            });
        }
    }

    return MS_RESULT_OK;
}

ms_result_code native_engine::set_active_image(const std::wstring& image_path)
{
    std::lock_guard lock(state_mutex_);
    active_image_ = image_path;
    loaded_image_path_.clear();
    active_image_bitmap_.Reset();
    clear_latest_inference_result_locked();
    ++image_generation_;
    update_status(L"Active image changed.");
    return MS_RESULT_OK;
}

ms_result_code native_engine::handle_input_event(const ms_input_event& input_event)
{
    std::lock_guard lock(state_mutex_);
    switch (input_event.type)
    {
    case MS_INPUT_MOUSE_MOVE:
        mouse_x_ = input_event.x;
        mouse_y_ = input_event.y;
        break;
    case MS_INPUT_MOUSE_WHEEL:
        zoom_ = std::clamp(zoom_ + (input_event.delta > 0 ? 0.1f : -0.1f), 0.1f, 8.0f);
        break;
    case MS_INPUT_RESIZE:
        width_ = input_event.width;
        height_ = input_event.height;
        discard_size_dependent_resources();
        break;
    default:
        break;
    }

    return MS_RESULT_OK;
}

ms_result_code native_engine::render(const ms_render_frame_args& args)
{
    width_ = args.width;
    height_ = args.height;

    auto result = initialize_device_resources();
    if (result != MS_RESULT_OK)
    {
        return result;
    }

    result = create_size_dependent_resources();
    if (result != MS_RESULT_OK)
    {
        return result;
    }

    result = ensure_active_image_bitmap();
    if (result != MS_RESULT_OK)
    {
        return result;
    }

    d2d_context_->BeginDraw();
    d2d_context_->Clear(clear_color);
    if (active_image_bitmap_)
    {
        d2d_context_->DrawBitmap(active_image_bitmap_.Get(), compute_image_dest_rect(), 1.0f, D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC);
    }
    render_overlay();
    const auto draw_result = d2d_context_->EndDraw();
    if (draw_result == D2DERR_RECREATE_TARGET)
    {
        discard_size_dependent_resources();
        return MS_RESULT_OK;
    }

    if (FAILED(draw_result))
    {
        return MS_RESULT_ERROR;
    }

    const auto present_result = swap_chain_->Present(1, 0);
    return FAILED(present_result) ? MS_RESULT_ERROR : MS_RESULT_OK;
}

ms_result_code native_engine::run_inference(const ms_inference_request& request)
{
    if (request.model_path == nullptr || request.task_name == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    std::error_code error;
    if (!std::filesystem::exists(request.model_path, error))
    {
        return MS_RESULT_NOT_FOUND;
    }

    const auto model_size = std::filesystem::file_size(request.model_path, error);
    if (error)
    {
        return MS_RESULT_ERROR;
    }

    if (model_size > to_bytes(performance_.max_vram_mb))
    {
        std::scoped_lock lock(state_mutex_);
        clear_latest_inference_result_locked();
        store_latest_inference_result_locked(
            0,
            active_image_,
            request.model_path,
            request.task_name,
            L"Rejected",
            L"VRAM budget",
            L"Model rejected because it exceeds the configured VRAM budget.",
            MS_RESULT_RESOURCE_LIMIT,
            {});
        update_status(L"Model rejected because it exceeds the configured VRAM budget.");
        return MS_RESULT_RESOURCE_LIMIT;
    }

    std::wstring active_image_path;
    D2D1_SIZE_F active_image_size{};
    {
        std::scoped_lock lock(state_mutex_);
        active_image_path = active_image_;
        active_image_size = active_image_size_;
    }

    if (active_image_path.empty())
    {
        std::scoped_lock lock(state_mutex_);
        clear_latest_inference_result_locked();
        store_latest_inference_result_locked(
            0,
            L"",
            request.model_path,
            request.task_name,
            L"Rejected",
            L"No active image",
            L"Inference rejected because no active image is selected.",
            MS_RESULT_NOT_FOUND,
            {});
        update_status(L"Inference rejected because no active image is selected.");
        return MS_RESULT_NOT_FOUND;
    }

    const auto generation = ++inference_generation_;
    const std::wstring model_path = request.model_path;
    const std::wstring task_name = request.task_name;
    const std::wstring cache_directory = std::filesystem::path(model_path).parent_path().empty()
        ? (std::filesystem::current_path() / L"trt_cache").wstring()
        : (std::filesystem::path(model_path).parent_path() / L"trt_cache").wstring();

    inference_executor_.submit([this, generation, active_image_path, active_image_size, model_path, task_name, model_size, cache_directory]
    {
        if (generation != inference_generation_.load())
        {
            return;
        }

        inference_run_result inference_result;
        std::wstring status_message;
        const inference_run_request backend_request
        {
            .model_path = model_path,
            .task_name = task_name,
            .image_path = active_image_path,
            .cache_directory = cache_directory,
            .image_size = active_image_size,
            .max_vram_mb = performance_.max_vram_mb
        };

        const auto backend_result = inference_backend_->run(backend_request, inference_result, status_message);
        std::scoped_lock lock(state_mutex_);
        if (generation != inference_generation_.load() || active_image_ != active_image_path)
        {
            return;
        }

        if (backend_result != MS_RESULT_OK)
        {
            store_latest_inference_result_locked(
                generation,
                active_image_path,
                model_path,
                task_name,
                inference_result.backend_name,
                inference_result.provider_name,
                status_message,
                backend_result,
                {});
            update_status(status_message.empty() ? L"Inference backend failed." : status_message);
            return;
        }

        vram_cache_.touch(model_path, static_cast<std::size_t>(std::min<std::uintmax_t>(model_size, to_bytes(performance_.max_vram_mb))));
        store_latest_inference_result_locked(
            generation,
            active_image_path,
            model_path,
            task_name,
            inference_result.backend_name,
            inference_result.provider_name,
            status_message,
            backend_result,
            std::move(inference_result.suggestions));
        update_status(status_message.empty()
            ? L"Inference results ready for " + task_name + L" using " + inference_result.backend_name + L"."
            : status_message);
    });

    return MS_RESULT_OK;
}

ms_result_code native_engine::get_latest_inference_summary(ms_inference_result_summary* summary) const
{
    if (summary == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    std::scoped_lock lock(state_mutex_);
    if (latest_inference_generation_ == 0 &&
        latest_inference_result_code_ == MS_RESULT_NOT_FOUND &&
        latest_inference_model_path_.empty() &&
        latest_inference_status_message_.empty())
    {
        return MS_RESULT_NOT_FOUND;
    }

    std::memset(summary, 0, sizeof(ms_inference_result_summary));
    copy_wstring(summary->active_image_path, latest_inference_active_image_);
    copy_wstring(summary->model_path, latest_inference_model_path_);
    copy_wstring(summary->task_name, latest_inference_task_name_);
    copy_wstring(summary->backend_name, latest_inference_backend_name_);
    copy_wstring(summary->provider_name, latest_inference_provider_name_);
    copy_wstring(summary->status_message, latest_inference_status_message_);
    summary->suggestion_count = static_cast<std::uint32_t>(latest_inference_suggestions_.size());
    summary->generation = latest_inference_generation_;
    summary->result_code = latest_inference_result_code_;
    return MS_RESULT_OK;
}

ms_result_code native_engine::get_latest_inference_suggestion(std::uint32_t index, ms_inference_suggestion* suggestion) const
{
    if (suggestion == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    std::scoped_lock lock(state_mutex_);
    if (index >= latest_inference_suggestions_.size())
    {
        return MS_RESULT_NOT_FOUND;
    }

    const auto& source = latest_inference_suggestions_[index];
    std::memset(suggestion, 0, sizeof(ms_inference_suggestion));
    suggestion->kind = source.kind;
    suggestion->confidence = source.confidence;
    suggestion->is_visible = source.is_visible ? 1U : 0U;
    copy_wstring(suggestion->id, source.id);
    copy_wstring(suggestion->label_name, source.label_name);
    copy_wstring(suggestion->suggested_label, source.suggested_label);
    suggestion->rect_x = source.rect_x;
    suggestion->rect_y = source.rect_y;
    suggestion->rect_width = source.rect_width;
    suggestion->rect_height = source.rect_height;
    suggestion->point_x = source.point_x;
    suggestion->point_y = source.point_y;
    return MS_RESULT_OK;
}

ms_result_code native_engine::set_view_transform(const ms_view_transform& transform)
{
    std::scoped_lock lock(state_mutex_);
    zoom_ = std::clamp(transform.zoom, 0.1f, 12.0f);
    pan_offset_x_ = transform.pan_offset_x;
    pan_offset_y_ = transform.pan_offset_y;
    return MS_RESULT_OK;
}

ms_result_code native_engine::export_annotations(const ms_export_request& request)
{
    if (request.output_path == nullptr || request.format_name == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    std::wofstream stream(request.output_path);
    if (!stream.is_open())
    {
        return MS_RESULT_ERROR;
    }

    std::scoped_lock lock(state_mutex_);
    stream << L"{\n";
    stream << L"  \"format\": \"" << request.format_name << L"\",\n";
    stream << L"  \"projectPath\": \"" << project_path_ << L"\",\n";
    stream << L"  \"imageCount\": " << images_.size() << L",\n";
    stream << L"  \"activeImage\": \"" << active_image_ << L"\"\n";
    stream << L"}\n";
    update_status(L"Annotation export stub completed.");
    return MS_RESULT_OK;
}

ms_result_code native_engine::initialize_device_resources()
{
    if (d3d_device_)
    {
        return MS_RESULT_OK;
    }

    UINT device_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    if (debug_layer_)
    {
        device_flags |= D3D11_CREATE_DEVICE_DEBUG;
    }

    D3D_FEATURE_LEVEL feature_levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL feature_level{};

    const auto d3d_result = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        device_flags,
        feature_levels,
        ARRAYSIZE(feature_levels),
        D3D11_SDK_VERSION,
        &d3d_device_,
        &feature_level,
        &d3d_context_);
    if (FAILED(d3d_result))
    {
        return MS_RESULT_ERROR;
    }

    const auto d2d_result = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d_factory_.ReleaseAndGetAddressOf());
    if (FAILED(d2d_result))
    {
        return MS_RESULT_ERROR;
    }

    ComPtr<IDXGIDevice> dxgi_device;
    if (FAILED(d3d_device_.As(&dxgi_device)))
    {
        return MS_RESULT_ERROR;
    }

    ComPtr<IDXGIAdapter> dxgi_adapter;
    DXGI_ADAPTER_DESC adapter_desc{};
    if (SUCCEEDED(dxgi_device->GetAdapter(&dxgi_adapter)) && SUCCEEDED(dxgi_adapter->GetDesc(&adapter_desc)))
    {
        gpu_name_ = adapter_desc.Description;
    }

    if (FAILED(d2d_factory_->CreateDevice(dxgi_device.Get(), &d2d_device_)))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(d2d_device_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &d2d_context_)))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(dwrite_factory_.ReleaseAndGetAddressOf()))))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic_factory_))))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(dwrite_factory_->CreateTextFormat(
        L"Segoe UI",
        nullptr,
        DWRITE_FONT_WEIGHT_SEMI_BOLD,
        DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,
        18.0f,
        L"",
        &text_format_)))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(d2d_context_->CreateSolidColorBrush(accent_color, &accent_brush_)) ||
        FAILED(d2d_context_->CreateSolidColorBrush(panel_color, &panel_brush_)))
    {
        return MS_RESULT_ERROR;
    }

    probe_runtime_support();

    return MS_RESULT_OK;
}

ms_result_code native_engine::ensure_active_image_bitmap()
{
    std::wstring active_image_path;
    {
        std::scoped_lock lock(state_mutex_);
        active_image_path = active_image_;
    }

    if (active_image_path.empty())
    {
        active_image_bitmap_.Reset();
        loaded_image_path_.clear();
        active_image_size_ = {};
        return MS_RESULT_OK;
    }

    if (active_image_bitmap_ && loaded_image_path_ == active_image_path)
    {
        return MS_RESULT_OK;
    }

    ComPtr<IWICBitmapDecoder> decoder;
    if (FAILED(wic_factory_->CreateDecoderFromFilename(active_image_path.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &decoder)))
    {
        update_status(L"Failed to decode active image.");
        return MS_RESULT_ERROR;
    }

    ComPtr<IWICBitmapFrameDecode> frame;
    if (FAILED(decoder->GetFrame(0, &frame)))
    {
        return MS_RESULT_ERROR;
    }

    ComPtr<IWICFormatConverter> converter;
    if (FAILED(wic_factory_->CreateFormatConverter(&converter)))
    {
        return MS_RESULT_ERROR;
    }

    if (FAILED(converter->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone, nullptr, 0.0f, WICBitmapPaletteTypeMedianCut)))
    {
        return MS_RESULT_ERROR;
    }

    active_image_bitmap_.Reset();
    if (FAILED(d2d_context_->CreateBitmapFromWicBitmap(converter.Get(), nullptr, &active_image_bitmap_)))
    {
        return MS_RESULT_ERROR;
    }

    loaded_image_path_ = active_image_path;
    active_image_size_ = active_image_bitmap_->GetSize();
    return MS_RESULT_OK;
}

ms_result_code native_engine::create_size_dependent_resources()
{
    if (swap_chain_ && d2d_target_bitmap_)
    {
        return MS_RESULT_OK;
    }

    ComPtr<IDXGIDevice> dxgi_device;
    ComPtr<IDXGIAdapter> dxgi_adapter;
    ComPtr<IDXGIFactory2> dxgi_factory;
    if (FAILED(d3d_device_.As(&dxgi_device)) ||
        FAILED(dxgi_device->GetAdapter(&dxgi_adapter)) ||
        FAILED(dxgi_adapter->GetParent(IID_PPV_ARGS(&dxgi_factory))))
    {
        return MS_RESULT_ERROR;
    }

    if (!swap_chain_)
    {
        DXGI_SWAP_CHAIN_DESC1 description{};
        description.Width = std::max(width_, 1U);
        description.Height = std::max(height_, 1U);
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        description.BufferCount = 2;
        description.SampleDesc.Count = 1;
        description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

        if (FAILED(dxgi_factory->CreateSwapChainForHwnd(
            d3d_device_.Get(),
            hwnd_,
            &description,
            nullptr,
            nullptr,
            &swap_chain_)))
        {
            return MS_RESULT_ERROR;
        }
    }
    else
    {
        swap_chain_->ResizeBuffers(0, std::max(width_, 1U), std::max(height_, 1U), DXGI_FORMAT_UNKNOWN, 0);
    }

    ComPtr<IDXGISurface> back_buffer;
    if (FAILED(swap_chain_->GetBuffer(0, IID_PPV_ARGS(&back_buffer))))
    {
        return MS_RESULT_ERROR;
    }

    const auto bitmap_properties = D2D1::BitmapProperties1(
        D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE));

    if (FAILED(d2d_context_->CreateBitmapFromDxgiSurface(back_buffer.Get(), &bitmap_properties, &d2d_target_bitmap_)))
    {
        return MS_RESULT_ERROR;
    }

    d2d_context_->SetTarget(d2d_target_bitmap_.Get());
    return MS_RESULT_OK;
}

void native_engine::discard_size_dependent_resources()
{
    if (d2d_context_)
    {
        d2d_context_->SetTarget(nullptr);
    }

    d2d_target_bitmap_.Reset();
    swap_chain_.Reset();
}

D2D1_RECT_F native_engine::compute_image_dest_rect() const
{
    if (active_image_size_.width <= 0.0f || active_image_size_.height <= 0.0f)
    {
        return D2D1::RectF(0, 0, 0, 0);
    }

    const auto scale_x = static_cast<float>(width_) / active_image_size_.width;
    const auto scale_y = static_cast<float>(height_) / active_image_size_.height;
    const auto scale = std::min(scale_x, scale_y) * zoom_;
    const auto render_width = active_image_size_.width * scale;
    const auto render_height = active_image_size_.height * scale;
    const auto offset_x = (static_cast<float>(width_) - render_width) / 2.0f + pan_offset_x_;
    const auto offset_y = (static_cast<float>(height_) - render_height) / 2.0f + pan_offset_y_;

    return D2D1::RectF(offset_x, offset_y, offset_x + render_width, offset_y + render_height);
}

void native_engine::render_overlay()
{
    // The desktop shell owns status and image metadata surfaces.
    // Keep the native viewport free from debug text so image annotation stays unobstructed.
}

void native_engine::probe_runtime_support()
{
    const auto cuda_module = LoadLibraryW(L"cudart64_12.dll");
    if (cuda_module != nullptr)
    {
        cuda_runtime_available_ = true;
        FreeLibrary(cuda_module);
    }

    const auto tensorrt_module = LoadLibraryW(L"nvinfer_10.dll");
    if (tensorrt_module != nullptr)
    {
        tensorrt_runtime_available_ = true;
        FreeLibrary(tensorrt_module);
    }
}

void native_engine::update_status(const std::wstring& status)
{
    last_status_ = status;
}

void native_engine::clear_latest_inference_result_locked()
{
    latest_inference_generation_ = 0;
    latest_inference_active_image_.clear();
    latest_inference_model_path_.clear();
    latest_inference_task_name_.clear();
    latest_inference_backend_name_.clear();
    latest_inference_provider_name_.clear();
    latest_inference_status_message_.clear();
    latest_inference_result_code_ = MS_RESULT_NOT_FOUND;
    latest_inference_suggestions_.clear();
}

void native_engine::store_latest_inference_result_locked(
    std::uint64_t generation,
    const std::wstring& image_path,
    const std::wstring& model_path,
    const std::wstring& task_name,
    const std::wstring& backend_name,
    const std::wstring& provider_name,
    const std::wstring& status_message,
    ms_result_code result_code,
    std::vector<native_inference_suggestion> suggestions)
{
    latest_inference_generation_ = generation;
    latest_inference_active_image_ = image_path;
    latest_inference_model_path_ = model_path;
    latest_inference_task_name_ = task_name;
    latest_inference_backend_name_ = backend_name;
    latest_inference_provider_name_ = provider_name;
    latest_inference_status_message_ = status_message;
    latest_inference_result_code_ = result_code;
    latest_inference_suggestions_ = std::move(suggestions);
}

void native_engine::reconfigure_workers()
{
    io_executor_.configure(std::max<std::uint32_t>(1, performance_.max_io_threads));
    decode_executor_.configure(std::max<std::uint32_t>(1, performance_.max_decode_threads));
    inference_executor_.configure(std::max<std::uint32_t>(1, performance_.max_inference_jobs));
}

std::vector<std::wstring> native_engine::split_lines(const std::wstring& blob)
{
    std::wstringstream stream(blob);
    std::wstring line;
    std::vector<std::wstring> lines;

    while (std::getline(stream, line))
    {
        if (!line.empty())
        {
            lines.push_back(line);
        }
    }

    return lines;
}

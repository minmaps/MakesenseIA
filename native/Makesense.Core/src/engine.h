#pragma once

#include "../include/ms_core_api.h"
#include "budgeted_lru.h"
#include "inference_backend.h"
#include "task_executor.h"

#include <d2d1_1.h>
#include <dwrite.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <atomic>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

struct stored_inference_result
{
    std::uint64_t generation{0};
    std::wstring active_image_path;
    std::wstring model_path;
    std::wstring task_name;
    std::wstring backend_name;
    std::wstring provider_name;
    std::wstring status_message;
    ms_result_code result_code{MS_RESULT_NOT_FOUND};
    bool is_complete{false};
    std::vector<native_inference_suggestion> suggestions;
};

class native_engine
{
public:
    native_engine(HWND hwnd, std::uint32_t width, std::uint32_t height, const ms_performance_config& config, bool debug_layer);
    ~native_engine();

    ms_result_code set_performance_limits(const ms_performance_config& config);
    ms_result_code open_project(const std::wstring& project_path);
    ms_result_code open_images(const std::wstring& image_paths_blob);
    ms_result_code set_active_image(const std::wstring& image_path);
    ms_result_code handle_input_event(const ms_input_event& input_event);
    ms_result_code render(const ms_render_frame_args& args);
    ms_result_code set_view_transform(const ms_view_transform& transform);
    ms_result_code run_inference(const ms_inference_request& request);
    ms_result_code run_inference_batch(const ms_inference_batch_request& request);
    ms_result_code get_latest_inference_summary(ms_inference_result_summary* summary) const;
    ms_result_code get_latest_inference_suggestion(std::uint32_t index, ms_inference_suggestion* suggestion) const;
    ms_result_code get_inference_batch_status(ms_inference_batch_status* status) const;
    ms_result_code get_inference_batch_result_summary(std::uint32_t index, ms_inference_result_summary* summary) const;
    ms_result_code get_inference_batch_result_suggestion(std::uint32_t result_index, std::uint32_t suggestion_index, ms_inference_suggestion* suggestion) const;
    ms_result_code export_annotations(const ms_export_request& request);
    void shutdown() noexcept;

private:
    ms_result_code initialize_device_resources();
    ms_result_code create_size_dependent_resources();
    ms_result_code ensure_active_image_bitmap();
    void discard_size_dependent_resources();
    void render_overlay();
    D2D1_RECT_F compute_image_dest_rect() const;
    void update_status(const std::wstring& status);
    void reconfigure_workers();
    void probe_runtime_support();
    void clear_latest_inference_result_locked();
    void clear_batch_inference_locked();
    void store_inference_summary(
        ms_inference_result_summary* summary,
        const stored_inference_result& source) const;
    void copy_inference_suggestion(
        ms_inference_suggestion* suggestion,
        const native_inference_suggestion& source) const;
    void store_latest_inference_result_locked(
        std::uint64_t generation,
        const std::wstring& image_path,
        const std::wstring& model_path,
        const std::wstring& task_name,
        const std::wstring& backend_name,
        const std::wstring& provider_name,
        const std::wstring& status_message,
        ms_result_code result_code,
        std::vector<native_inference_suggestion> suggestions);
    static std::vector<std::wstring> split_lines(const std::wstring& blob);

    HWND hwnd_{};
    std::uint32_t width_{};
    std::uint32_t height_{};
    ms_performance_config performance_{};
    bool debug_layer_{false};
    float mouse_x_{0.0f};
    float mouse_y_{0.0f};
    float zoom_{1.0f};
    float pan_offset_x_{0.0f};
    float pan_offset_y_{0.0f};

    mutable std::mutex state_mutex_;
    std::wstring project_path_;
    std::vector<std::wstring> images_;
    std::wstring active_image_;
    std::wstring last_status_{L"Native engine initialized."};
    std::wstring gpu_name_{L"(unknown)"};
    bool cuda_runtime_available_{false};
    bool tensorrt_runtime_available_{false};

    std::atomic<std::uint64_t> image_generation_{0};
    std::atomic<std::uint64_t> inference_generation_{0};
    std::uint64_t latest_inference_generation_{0};
    std::wstring latest_inference_active_image_;
    std::wstring latest_inference_model_path_;
    std::wstring latest_inference_task_name_;
    std::wstring latest_inference_backend_name_;
    std::wstring latest_inference_provider_name_;
    std::wstring latest_inference_status_message_;
    ms_result_code latest_inference_result_code_{MS_RESULT_NOT_FOUND};
    std::vector<native_inference_suggestion> latest_inference_suggestions_;
    std::uint64_t batch_inference_generation_{0};
    std::uint32_t batch_inference_completed_{0};
    bool batch_inference_running_{false};
    ms_result_code batch_inference_result_code_{MS_RESULT_NOT_FOUND};
    std::wstring batch_inference_status_message_;
    std::vector<stored_inference_result> batch_inference_results_;
    std::vector<std::uint32_t> batch_inference_completed_indices_;

    budgeted_lru ram_cache_;
    budgeted_lru vram_cache_;
    task_executor io_executor_;
    task_executor decode_executor_;
    task_executor inference_executor_;
    std::unique_ptr<inference_backend> inference_backend_;

    Microsoft::WRL::ComPtr<ID3D11Device> d3d_device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> d3d_context_;
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swap_chain_;
    Microsoft::WRL::ComPtr<ID2D1Factory1> d2d_factory_;
    Microsoft::WRL::ComPtr<ID2D1Device> d2d_device_;
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> d2d_context_;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> d2d_target_bitmap_;
    Microsoft::WRL::ComPtr<IWICImagingFactory> wic_factory_;
    Microsoft::WRL::ComPtr<ID2D1Bitmap1> active_image_bitmap_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> accent_brush_;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> panel_brush_;
    Microsoft::WRL::ComPtr<IDWriteFactory> dwrite_factory_;
    Microsoft::WRL::ComPtr<IDWriteTextFormat> text_format_;
    std::wstring loaded_image_path_;
    D2D1_SIZE_F active_image_size_{};
};

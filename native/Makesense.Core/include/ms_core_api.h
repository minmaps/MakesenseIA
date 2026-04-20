#pragma once

#include <cstdint>
#include <windows.h>

#ifdef MAKESENSE_CORE_EXPORTS
#define MS_CORE_API __declspec(dllexport)
#else
#define MS_CORE_API __declspec(dllimport)
#endif

enum ms_result_code : int
{
    MS_RESULT_OK = 0,
    MS_RESULT_ERROR = 1,
    MS_RESULT_INVALID_ARGUMENT = 2,
    MS_RESULT_NOT_SUPPORTED = 3,
    MS_RESULT_RESOURCE_LIMIT = 4,
    MS_RESULT_NOT_FOUND = 5
};

enum ms_input_event_type : std::uint32_t
{
    MS_INPUT_NONE = 0,
    MS_INPUT_MOUSE_MOVE = 1,
    MS_INPUT_MOUSE_DOWN = 2,
    MS_INPUT_MOUSE_UP = 3,
    MS_INPUT_MOUSE_WHEEL = 4,
    MS_INPUT_RESIZE = 5
};

enum ms_mouse_button : std::uint32_t
{
    MS_MOUSE_NONE = 0,
    MS_MOUSE_LEFT = 1,
    MS_MOUSE_RIGHT = 2,
    MS_MOUSE_MIDDLE = 3
};

enum ms_inference_suggestion_kind : std::uint32_t
{
    MS_INFERENCE_SUGGESTION_RECT = 0,
    MS_INFERENCE_SUGGESTION_POINT = 1
};

struct ms_performance_config
{
    std::uint32_t max_ram_mb;
    std::uint32_t max_vram_mb;
    std::uint32_t max_decode_threads;
    std::uint32_t max_io_threads;
    std::uint32_t max_inference_jobs;
    std::uint32_t max_prefetch_images;
};

struct ms_create_engine_args
{
    HWND target_hwnd;
    std::uint32_t width;
    std::uint32_t height;
    ms_performance_config performance;
    std::uint8_t enable_debug_layer;
};

struct ms_input_event
{
    ms_input_event_type type;
    float x;
    float y;
    float delta;
    ms_mouse_button button;
    std::uint32_t width;
    std::uint32_t height;
};

struct ms_render_frame_args
{
    std::uint32_t width;
    std::uint32_t height;
};

struct ms_view_transform
{
    float zoom;
    float pan_offset_x;
    float pan_offset_y;
};

struct ms_inference_request
{
    const wchar_t* model_path;
    const wchar_t* task_name;
};

struct ms_inference_result_summary
{
    wchar_t active_image_path[260];
    wchar_t model_path[260];
    wchar_t task_name[64];
    std::uint32_t suggestion_count;
    std::uint64_t generation;
};

struct ms_inference_suggestion
{
    ms_inference_suggestion_kind kind;
    float confidence;
    std::uint8_t is_visible;
    std::uint8_t reserved[3];
    wchar_t id[40];
    wchar_t label_name[64];
    wchar_t suggested_label[64];
    float rect_x;
    float rect_y;
    float rect_width;
    float rect_height;
    float point_x;
    float point_y;
};

struct ms_export_request
{
    const wchar_t* output_path;
    const wchar_t* format_name;
};

extern "C"
{
    MS_CORE_API void* ms_create_engine(const ms_create_engine_args* args);
    MS_CORE_API ms_result_code ms_set_performance_limits(void* engine, const ms_performance_config* config);
    MS_CORE_API ms_result_code ms_open_project(void* engine, const wchar_t* project_path);
    MS_CORE_API ms_result_code ms_open_images(void* engine, const wchar_t* image_paths_blob);
    MS_CORE_API ms_result_code ms_set_active_image(void* engine, const wchar_t* image_path);
    MS_CORE_API ms_result_code ms_handle_input_event(void* engine, const ms_input_event* input_event);
    MS_CORE_API ms_result_code ms_render(void* engine, const ms_render_frame_args* args);
    MS_CORE_API ms_result_code ms_set_view_transform(void* engine, const ms_view_transform* transform);
    MS_CORE_API ms_result_code ms_run_inference(void* engine, const ms_inference_request* request);
    MS_CORE_API ms_result_code ms_get_latest_inference_summary(void* engine, ms_inference_result_summary* summary);
    MS_CORE_API ms_result_code ms_get_latest_inference_suggestion(void* engine, std::uint32_t index, ms_inference_suggestion* suggestion);
    MS_CORE_API ms_result_code ms_export_annotations(void* engine, const ms_export_request* request);
    MS_CORE_API void ms_shutdown(void* engine);
}

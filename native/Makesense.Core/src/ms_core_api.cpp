#define MAKESENSE_CORE_EXPORTS

#include "../include/ms_core_api.h"
#include "engine.h"

namespace
{
    native_engine* as_engine(void* handle)
    {
        return static_cast<native_engine*>(handle);
    }

    template <typename T>
    void* guard_handle(T&& action)
    {
        try
        {
            return action();
        }
        catch (...)
        {
            return nullptr;
        }
    }

    template <typename T>
    ms_result_code guard_result(T&& action)
    {
        try
        {
            return action();
        }
        catch (...)
        {
            return MS_RESULT_ERROR;
        }
    }
}

extern "C" MS_CORE_API void* ms_create_engine(const ms_create_engine_args* args)
{
    if (args == nullptr || args->target_hwnd == nullptr)
    {
        return nullptr;
    }

    return guard_handle([&]
    {
        return new native_engine(args->target_hwnd, args->width, args->height, args->performance, args->enable_debug_layer != 0);
    });
}

extern "C" MS_CORE_API ms_result_code ms_set_performance_limits(void* engine, const ms_performance_config* config)
{
    if (engine == nullptr || config == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->set_performance_limits(*config);
    });
}

extern "C" MS_CORE_API ms_result_code ms_open_project(void* engine, const wchar_t* project_path)
{
    if (engine == nullptr || project_path == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->open_project(project_path);
    });
}

extern "C" MS_CORE_API ms_result_code ms_open_images(void* engine, const wchar_t* image_paths_blob)
{
    if (engine == nullptr || image_paths_blob == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->open_images(image_paths_blob);
    });
}

extern "C" MS_CORE_API ms_result_code ms_set_active_image(void* engine, const wchar_t* image_path)
{
    if (engine == nullptr || image_path == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->set_active_image(image_path);
    });
}

extern "C" MS_CORE_API ms_result_code ms_handle_input_event(void* engine, const ms_input_event* input_event)
{
    if (engine == nullptr || input_event == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->handle_input_event(*input_event);
    });
}

extern "C" MS_CORE_API ms_result_code ms_render(void* engine, const ms_render_frame_args* args)
{
    if (engine == nullptr || args == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->render(*args);
    });
}

extern "C" MS_CORE_API ms_result_code ms_set_view_transform(void* engine, const ms_view_transform* transform)
{
    if (engine == nullptr || transform == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->set_view_transform(*transform);
    });
}

extern "C" MS_CORE_API ms_result_code ms_run_inference(void* engine, const ms_inference_request* request)
{
    if (engine == nullptr || request == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->run_inference(*request);
    });
}

extern "C" MS_CORE_API ms_result_code ms_get_latest_inference_summary(void* engine, ms_inference_result_summary* summary)
{
    if (engine == nullptr || summary == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->get_latest_inference_summary(summary);
    });
}

extern "C" MS_CORE_API ms_result_code ms_get_latest_inference_suggestion(void* engine, std::uint32_t index, ms_inference_suggestion* suggestion)
{
    if (engine == nullptr || suggestion == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->get_latest_inference_suggestion(index, suggestion);
    });
}

extern "C" MS_CORE_API ms_result_code ms_export_annotations(void* engine, const ms_export_request* request)
{
    if (engine == nullptr || request == nullptr)
    {
        return MS_RESULT_INVALID_ARGUMENT;
    }

    return guard_result([&]
    {
        return as_engine(engine)->export_annotations(*request);
    });
}

extern "C" MS_CORE_API void ms_shutdown(void* engine)
{
    try
    {
        delete as_engine(engine);
    }
    catch (...)
    {
    }
}

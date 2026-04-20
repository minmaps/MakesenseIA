#pragma once

#include "../include/ms_core_api.h"

#include <d2d1.h>

#include <memory>
#include <string>
#include <vector>

struct native_inference_suggestion
{
    ms_inference_suggestion_kind kind{MS_INFERENCE_SUGGESTION_RECT};
    float confidence{0.0f};
    bool is_visible{true};
    std::wstring id;
    std::wstring label_name;
    std::wstring suggested_label;
    float rect_x{0.0f};
    float rect_y{0.0f};
    float rect_width{0.0f};
    float rect_height{0.0f};
    float point_x{0.0f};
    float point_y{0.0f};
};

struct inference_run_request
{
    std::wstring model_path;
    std::wstring task_name;
    std::wstring image_path;
    std::wstring cache_directory;
    D2D1_SIZE_F image_size{};
    std::uint32_t max_vram_mb{0};
};

struct inference_run_result
{
    std::wstring backend_name;
    std::wstring provider_name;
    std::vector<native_inference_suggestion> suggestions;
};

class inference_backend
{
public:
    virtual ~inference_backend() = default;

    virtual bool available() const = 0;
    virtual std::wstring backend_summary() const = 0;
    virtual ms_result_code run(const inference_run_request& request, inference_run_result& result, std::wstring& status) = 0;
};

std::unique_ptr<inference_backend> create_inference_backend();
std::vector<native_inference_suggestion> build_synthetic_inference_suggestions(
    const std::wstring& image_path,
    const std::wstring& model_path,
    const std::wstring& task_name,
    D2D1_SIZE_F image_size);

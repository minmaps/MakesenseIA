#include "inference_backend.h"

namespace
{
    class unavailable_inference_backend final : public inference_backend
    {
    public:
        bool available() const override
        {
            return false;
        }

        std::wstring backend_summary() const override
        {
            return L"ONNX Runtime unavailable";
        }

        ms_result_code run(const inference_run_request& request, inference_run_result& result, std::wstring& status) override
        {
            (void)request;
            result.backend_name = L"Unavailable";
            result.provider_name = L"Fallback";
            result.result_code = MS_RESULT_NOT_SUPPORTED;
            result.status_message = L"ONNX Runtime is not bundled in this build. Rebuild Makesense.Core with ONNX Runtime to run real inference.";
            result.suggestions.clear();
            status = result.status_message;
            return result.result_code;
        }
    };
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

    return std::make_unique<unavailable_inference_backend>();
}

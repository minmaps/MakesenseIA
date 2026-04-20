cmake_minimum_required(VERSION 3.24)

project(MakesenseCore LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(MS_ENABLE_ONNXRUNTIME_TRT "Enable ONNX Runtime backend with TensorRT/CUDA execution providers" OFF)
set(ONNXRUNTIME_ROOT "" CACHE PATH "Path to the ONNX Runtime installation root")

add_library(Makesense.Core SHARED
    src/ms_core_api.cpp
    src/engine.cpp
    src/inference_backend_stub.cpp
    src/task_executor.cpp
)

target_include_directories(Makesense.Core
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_definitions(Makesense.Core PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_options(Makesense.Core PRIVATE /W4 /EHsc)

target_link_libraries(Makesense.Core
    PRIVATE
        d3d11
        dxgi
        d2d1
        dwrite
        windowscodecs
)

if(MS_ENABLE_ONNXRUNTIME_TRT)
    if(NOT ONNXRUNTIME_ROOT)
        if(DEFINED ENV{ONNXRUNTIME_ROOT})
            set(ONNXRUNTIME_ROOT "$ENV{ONNXRUNTIME_ROOT}")
        elseif(EXISTS "C:/onnxruntime/include" AND EXISTS "C:/onnxruntime/lib/onnxruntime.lib")
            set(ONNXRUNTIME_ROOT "C:/onnxruntime")
        endif()
    endif()

    if(ONNXRUNTIME_ROOT AND EXISTS "${ONNXRUNTIME_ROOT}/include/onnxruntime_cxx_api.h" AND EXISTS "${ONNXRUNTIME_ROOT}/lib/onnxruntime.lib")
        target_sources(Makesense.Core PRIVATE src/onnxruntime_inference_backend.cpp)
        target_include_directories(Makesense.Core PRIVATE "${ONNXRUNTIME_ROOT}/include")
        target_link_directories(Makesense.Core PRIVATE "${ONNXRUNTIME_ROOT}/lib")
        target_link_libraries(Makesense.Core PRIVATE onnxruntime.lib)
        target_compile_definitions(Makesense.Core PRIVATE MS_WITH_ONNXRUNTIME=1)
        message(STATUS "Makesense.Core: ONNX Runtime TensorRT/CUDA backend enabled from ${ONNXRUNTIME_ROOT}")
    else()
        message(WARNING "MS_ENABLE_ONNXRUNTIME_TRT is ON but ONNX Runtime was not found. Building with synthetic inference backend.")
    endif()
endif()

set_target_properties(Makesense.Core PROPERTIES
    OUTPUT_NAME "Makesense.Core"
)

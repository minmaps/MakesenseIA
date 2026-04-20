param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$nativeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$coreSource = Join-Path $nativeRoot "Makesense.Core"
$buildRoot = Join-Path $nativeRoot "build\$Configuration"
$artifactRoot = Join-Path $nativeRoot "artifacts\core\$Configuration"
$solutionPath = Join-Path (Split-Path -Parent $nativeRoot) "Makesense.Native.slnx"
$ortRoot = if ($env:ONNXRUNTIME_ROOT -and (Test-Path $env:ONNXRUNTIME_ROOT)) { $env:ONNXRUNTIME_ROOT } elseif (Test-Path "C:\onnxruntime") { "C:\onnxruntime" } else { $null }
$cudaRoot = if ($env:CUDA_PATH -and (Test-Path $env:CUDA_PATH)) { $env:CUDA_PATH } elseif (Test-Path "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6") { "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.6" } else { $null }
$tensorRtCandidates = @(
    $env:TENSORRT_ROOT,
    "C:\TensorRT",
    "C:\Users\RAIDABLE\Desktop\IA-finals",
    "C:\Python314\Lib\site-packages\tensorrt_libs"
) | Where-Object { $_ -and (Test-Path $_) }

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$cmakeArgs = @(
    "-S", $coreSource,
    "-B", $buildRoot,
    "-G", "Visual Studio 18 2026",
    "-A", "x64"
)

if ($ortRoot) {
    $cmakeArgs += @("-DMS_ENABLE_ONNXRUNTIME_TRT=ON", "-DONNXRUNTIME_ROOT=$ortRoot")
}

cmake @cmakeArgs
cmake --build $buildRoot --config $Configuration

Get-ChildItem -Path $artifactRoot -File -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item (Join-Path $buildRoot "$Configuration\Makesense.Core.dll") $artifactRoot -Force
if (Test-Path (Join-Path $buildRoot "$Configuration\Makesense.Core.pdb")) {
    Copy-Item (Join-Path $buildRoot "$Configuration\Makesense.Core.pdb") $artifactRoot -Force
}

if ($ortRoot -and (Test-Path (Join-Path $ortRoot "lib"))) {
    Get-ChildItem -Path (Join-Path $ortRoot "lib") -Filter "*.dll" -File | ForEach-Object {
        Copy-Item $_.FullName $artifactRoot -Force
    }
}

foreach ($candidate in $tensorRtCandidates) {
    Get-ChildItem -Path $candidate -Filter "*.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(nvinfer|nvonnxparser|cudart|cublas|cublasLt|cudnn|cufft|curand|cusolver|cusparse)' } |
        ForEach-Object { Copy-Item $_.FullName $artifactRoot -Force }
}

if ($cudaRoot -and (Test-Path (Join-Path $cudaRoot "bin"))) {
    Get-ChildItem -Path (Join-Path $cudaRoot "bin") -Filter "*.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(cudart|cublas|cublasLt|cufft|curand|cusolver|cusparse)' } |
        ForEach-Object {
            if (-not (Test-Path (Join-Path $artifactRoot $_.Name))) {
                Copy-Item $_.FullName $artifactRoot -Force
            }
        }
}

dotnet build $solutionPath -c $Configuration -p:UseSharedCompilation=false

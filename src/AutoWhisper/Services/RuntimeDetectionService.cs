using System.Runtime.InteropServices;

namespace AutoWhisper.Services;

public enum GpuRuntime
{
    Cpu,
    Cuda,
    Vulkan
}

public static class RuntimeDetectionService
{
    private static readonly Lazy<GpuRuntime> s_cached =
        new(DetectBestRuntimeInternal, LazyThreadSafetyMode.ExecutionAndPublication);

    public static GpuRuntime DetectBestRuntime() => s_cached.Value;

    private static GpuRuntime DetectBestRuntimeInternal()
    {
        if (IsCudaAvailable())
            return GpuRuntime.Cuda;

        if (IsVulkanAvailable())
            return GpuRuntime.Vulkan;

        return GpuRuntime.Cpu;
    }

    private static bool IsCudaAvailable()
    {
        try
        {
            var handle = NativeLibrary.Load("nvcuda.dll");
            NativeLibrary.Free(handle);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Unexpected error during CUDA detection: {ex.Message}");
            return false;
        }
    }

    private static bool IsVulkanAvailable()
    {
        try
        {
            var handle = NativeLibrary.Load("vulkan-1.dll");
            NativeLibrary.Free(handle);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Unexpected error during Vulkan detection: {ex.Message}");
            return false;
        }
    }

    public static string GetRuntimeDisplayName(GpuRuntime runtime) => runtime switch
    {
        GpuRuntime.Cuda => "NVIDIA CUDA",
        GpuRuntime.Vulkan => "Vulkan (AMD/Intel/NVIDIA)",
        GpuRuntime.Cpu => "CPU",
        _ => "Unknown"
    };
}

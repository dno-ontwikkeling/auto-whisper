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
    public static GpuRuntime DetectBestRuntime()
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
        catch
        {
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
        catch
        {
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

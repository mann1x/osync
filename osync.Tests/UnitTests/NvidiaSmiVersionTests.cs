using FluentAssertions;

namespace osync.Tests.UnitTests;

public class NvidiaSmiVersionTests
{
    [Fact]
    public void OldHeader_DriverAndCudaVersion()
    {
        const string output = """
            Fri Oct  9 20:11:28 2026
            +-----------------------------------------------------------------------------------------+
            | NVIDIA-SMI 591.74                 Driver Version: 591.74         CUDA Version: 13.1     |
            +-----------------------------------------+------------------------+----------------------+
            """;
        NvidiaGpuProvider.ParseNvidiaSmiVersions(output).Should().Be(("13.1", "591.74"));
    }

    [Fact]
    public void NewHeader_KmdAndCudaUmdVersion()
    {
        // nvidia-smi 617.42: "Driver Version" and "CUDA Version" are deprecated
        const string output = """
            Fri Oct  9 20:11:28 2026
            +-----------------------------------------------------------------------------------------+
            | NVIDIA-SMI 617.42                 KMD Version: 617.42        CUDA UMD Version: 13.4     |
            +-----------------------------------------+------------------------+----------------------+
            """;
        NvidiaGpuProvider.ParseNvidiaSmiVersions(output).Should().Be(("13.4", "617.42"));
    }

    [Fact]
    public void NoHeader_Empty()
    {
        NvidiaGpuProvider.ParseNvidiaSmiVersions("No devices were found\n").Should().Be(("", ""));
    }
}

using System.Runtime.InteropServices;
using PeekDows.Core.Win32;
using Xunit;

namespace PeekDows.Tests;

public class MonitorNativeMethodsTests
{
    [Fact]
    public void MONITORINFOEX_CbSize_IsNonZero()
    {
        var mi = new MonitorNativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);
        Assert.NotEqual(0, mi.cbSize);
    }

    [Fact]
    public void MONITORINFOEX_CbSize_MatchesMarshalSize()
    {
        var mi = new MonitorNativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);
        Assert.Equal(Marshal.SizeOf<MonitorNativeMethods.MONITORINFOEX>(), mi.cbSize);
    }

    [Fact]
    public void MONITORINFOEX_DefaultValues_AreZeroOrNullOrDefault()
    {
        var mi = new MonitorNativeMethods.MONITORINFOEX();
        Assert.Equal(0, mi.cbSize);
        Assert.Equal(0u, mi.dwFlags);
    }
}

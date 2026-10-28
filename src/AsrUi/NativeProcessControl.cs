using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AsrUi;

/// <summary>
/// Windows does not expose process suspension through the managed Process API.
/// Suspending the child process keeps its RAM, GPU context and on-disk job workspace intact,
/// so resume continues at the exact current FFmpeg/CrispASR instruction rather than restarting.
/// </summary>
internal static class NativeProcessControl
{
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    public static bool TrySuspend(Process process, out string? error)
    {
        return TryChangeState(process, suspend: true, out error);
    }

    public static bool TryResume(Process process, out string? error)
    {
        return TryChangeState(process, suspend: false, out error);
    }

    private static bool TryChangeState(Process process, bool suspend, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "暂停/继续仅支持 Windows 发布包。";
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                error = "当前外部进程已经结束。";
                return false;
            }

            var status = suspend ? NtSuspendProcess(process.Handle) : NtResumeProcess(process.Handle);
            if (status == 0) return true;

            error = $"Windows 返回 NTSTATUS 0x{status:X8}";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ExternalException)
        {
            error = ex.Message;
            return false;
        }
    }
}

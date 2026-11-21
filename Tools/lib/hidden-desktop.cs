// Win32 hidden desktop runner (used by Tools/window-probe.mjs through PowerShell Add-Type; keep this file ASCII and C# 5).
// Creates a separate desktop in the current window station, starts one process on it (STARTUPINFO.lpDesktop), and while it runs
// samples the user's desktop: foreground window, top-level windows owned by the started process tree, input desktop name.
// The process tree lives in a job object (kill on close), so nothing survives this call.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class HiddenDesk
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
        public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    delegate bool EnumProc(IntPtr hwnd, IntPtr lp);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll")]
    static extern IntPtr GetThreadDesktop(uint tid);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder buf, int len, out int needed);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EnumDesktopWindows(IntPtr desk, EnumProc f, IntPtr lp);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr sa, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int len);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryInformationJobObject(IntPtr job, int cls, IntPtr info, int len, IntPtr ret);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    const uint GENERIC_ALL = 0x10000000;
    const uint CREATE_SUSPENDED = 0x4, CREATE_NO_WINDOW = 0x08000000, BELOW_NORMAL_PRIORITY_CLASS = 0x4000, CREATE_UNICODE_ENVIRONMENT = 0x400;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    const int JobObjectBasicProcessIdList = 3, JobObjectExtendedLimitInformation = 9, UOI_NAME = 2;

    static string DesktopName(IntPtr desk)
    {
        if (desk == IntPtr.Zero) return "";
        var sb = new StringBuilder(256);
        int needed;
        return GetUserObjectInformation(desk, UOI_NAME, sb, sb.Capacity * 2, out needed) ? sb.ToString() : "";
    }

    static string InputDesktopName()
    {
        IntPtr h = OpenInputDesktop(0, false, 1);   // DESKTOP_READOBJECTS
        if (h == IntPtr.Zero) return "?";           // secure desktop (lock screen, UAC) or no access
        string name = DesktopName(h);
        CloseDesktop(h);
        return name;
    }

    static HashSet<uint> JobPids(IntPtr job)
    {
        var set = new HashSet<uint>();
        int size = 8 + IntPtr.Size * 256;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, JobObjectBasicProcessIdList, buf, size, IntPtr.Zero)) return set;
            int n = Marshal.ReadInt32(buf, 4);
            for (int i = 0; i < n && i < 256; i++) set.Add((uint)Marshal.ReadIntPtr(buf, 8 + i * IntPtr.Size).ToInt64());
        }
        finally { Marshal.FreeHGlobal(buf); }
        return set;
    }

    static List<IntPtr> Windows(IntPtr desk)
    {
        var list = new List<IntPtr>();
        EnumDesktopWindows(desk, delegate(IntPtr hwnd, IntPtr lp) { list.Add(hwnd); return true; }, IntPtr.Zero);
        return list;
    }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// Run commandLine on a new desktop named desktopName and return a JSON report. Never switches desktops and never touches windows of the user's desktop.
    /// The report has no titles of the user's windows (only handles and process ids).
    /// </summary>
    public static string Run(string desktopName, string commandLine, string cwd, int timeoutMs, int sampleMs)
    {
        var o = new StringBuilder("{");
        SetProcessDPIAware();   // report client sizes in physical pixels
        IntPtr mine = GetThreadDesktop(GetCurrentThreadId());
        string mineName = DesktopName(mine);
        IntPtr fgBefore = GetForegroundWindow();
        uint fgBeforePid;
        GetWindowThreadProcessId(fgBefore, out fgBeforePid);
        int userWindowsBefore = 0;
        foreach (IntPtr w in Windows(mine)) if (IsWindowVisible(w)) userWindowsBefore++;
        o.Append("\"desktop\":").Append(Json(desktopName)).Append(",\"callerDesktop\":").Append(Json(mineName))
         .Append(",\"inputDesktopBefore\":").Append(Json(InputDesktopName()))
         .Append(",\"foregroundBefore\":").Append(fgBefore.ToInt64()).Append(",\"foregroundBeforePid\":").Append(fgBeforePid)
         .Append(",\"userVisibleWindowsBefore\":").Append(userWindowsBefore);

        IntPtr desk = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
        if (desk == IntPtr.Zero) return o.Append(",\"ok\":false,\"error\":\"CreateDesktop ").Append(Marshal.GetLastWin32Error()).Append("\"}").ToString();
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        var limit = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limit.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limit, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));

        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.lpDesktop = desktopName;   // same window station as the caller
        PROCESS_INFORMATION pi;
        bool started = CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
            CREATE_SUSPENDED | CREATE_NO_WINDOW | BELOW_NORMAL_PRIORITY_CLASS | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, cwd, ref si, out pi);
        if (!started)
        {
            int err = Marshal.GetLastWin32Error();
            CloseHandle(job);
            CloseDesktop(desk);
            return o.Append(",\"ok\":false,\"error\":\"CreateProcess ").Append(err).Append("\"}").ToString();
        }
        bool inJob = AssignProcessToJobObject(job, pi.hProcess);
        ResumeThread(pi.hThread);

        int samples = 0, foregroundOurs = 0, foregroundChanged = 0, oursOnUserDesktop = 0, hiddenMax = 0, hiddenVisibleMax = 0, maxW = 0, maxH = 0, inputNotDefault = 0;
        var titles = new List<string>();
        var sizesSeen = new List<string>();
        var stylesSeen = new List<string>();   // GWL_STYLE of every visible window wider than 64 px (hex) - the caller checks that none can be resized by its border
        bool timedOut = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            bool exited = WaitForSingleObject(pi.hProcess, (uint)sampleMs) == 0;
            samples++;
            var pids = JobPids(job);
            pids.Add(pi.dwProcessId);
            IntPtr fg = GetForegroundWindow();
            uint fgPid;
            GetWindowThreadProcessId(fg, out fgPid);
            if (fg != IntPtr.Zero && pids.Contains(fgPid)) foregroundOurs++;
            if (fg != fgBefore) foregroundChanged++;
            if (InputDesktopName() != mineName) inputNotDefault++;
            int onUser = 0;
            foreach (IntPtr w in Windows(mine))
            {
                uint pid;
                GetWindowThreadProcessId(w, out pid);
                if (pids.Contains(pid)) onUser++;
            }
            oursOnUserDesktop = Math.Max(oursOnUserDesktop, onUser);
            int hidden = 0, hiddenVisible = 0;
            foreach (IntPtr w in Windows(desk))
            {
                uint pid;
                GetWindowThreadProcessId(w, out pid);
                if (!pids.Contains(pid)) continue;
                hidden++;
                if (!IsWindowVisible(w)) continue;
                hiddenVisible++;
                RECT r;
                if (GetClientRect(w, out r))
                {
                    string size = (r.right - r.left) + "x" + (r.bottom - r.top);
                    if (r.right - r.left > 64 && !sizesSeen.Contains(size)) sizesSeen.Add(size);
                    string style = ((uint)GetWindowLong(w, -16)).ToString("x8");
                    if (r.right - r.left > 64 && !stylesSeen.Contains(style)) stylesSeen.Add(style);
                    if ((long)(r.right - r.left) * (r.bottom - r.top) > (long)maxW * maxH) { maxW = r.right - r.left; maxH = r.bottom - r.top; }
                }
                var sb = new StringBuilder(128);
                GetWindowText(w, sb, sb.Capacity);
                if (sb.Length > 0 && !titles.Contains(sb.ToString())) titles.Add(sb.ToString());
            }
            hiddenMax = Math.Max(hiddenMax, hidden);
            hiddenVisibleMax = Math.Max(hiddenVisibleMax, hiddenVisible);
            if (exited) break;
            if (clock.ElapsedMilliseconds > timeoutMs) { timedOut = true; break; }
        }
        uint code = 0;
        if (!timedOut) GetExitCodeProcess(pi.hProcess, out code);
        TerminateJobObject(job, 99);
        if (timedOut) WaitForSingleObject(pi.hProcess, 5000);
        int remaining = -1;
        for (int i = 0; i < 50; i++)
        {
            remaining = JobPids(job).Count;
            if (remaining == 0) break;
            Thread.Sleep(100);
        }
        IntPtr fgAfter = GetForegroundWindow();
        int userWindowsAfter = 0;
        foreach (IntPtr w in Windows(mine)) if (IsWindowVisible(w)) userWindowsAfter++;
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseHandle(job);
        bool closed = CloseDesktop(desk);

        o.Append(",\"pid\":").Append(pi.dwProcessId).Append(",\"inJob\":").Append(inJob ? "true" : "false")
         .Append(",\"exitCode\":").Append(timedOut ? "null" : ((int)code).ToString()).Append(",\"timedOut\":").Append(timedOut ? "true" : "false")
         .Append(",\"seconds\":").Append((clock.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
         .Append(",\"samples\":").Append(samples)
         .Append(",\"hiddenWindowsMax\":").Append(hiddenMax).Append(",\"hiddenVisibleWindowsMax\":").Append(hiddenVisibleMax)
         .Append(",\"hiddenLargestClient\":[").Append(maxW).Append(',').Append(maxH).Append(']');
        o.Append(",\"hiddenClientSizes\":[");
        for (int i = 0; i < sizesSeen.Count; i++) o.Append(i > 0 ? "," : "").Append(Json(sizesSeen[i]));
        o.Append("],\"hiddenWindowStyles\":[");
        for (int i = 0; i < stylesSeen.Count; i++) o.Append(i > 0 ? "," : "").Append(Json(stylesSeen[i]));
        o.Append("],\"hiddenTitles\":[");
        for (int i = 0; i < titles.Count; i++) o.Append(i > 0 ? "," : "").Append(Json(titles[i]));
        o.Append("]")
         .Append(",\"oursOnUserDesktopMax\":").Append(oursOnUserDesktop)
         .Append(",\"foregroundOursSamples\":").Append(foregroundOurs)
         .Append(",\"foregroundChangedSamples\":").Append(foregroundChanged)
         .Append(",\"foregroundAfter\":").Append(fgAfter.ToInt64()).Append(",\"foregroundSame\":").Append(fgAfter == fgBefore ? "true" : "false")
         .Append(",\"inputDesktopNotCallerSamples\":").Append(inputNotDefault)
         .Append(",\"inputDesktopAfter\":").Append(Json(InputDesktopName()))
         .Append(",\"userVisibleWindowsAfter\":").Append(userWindowsAfter)
         .Append(",\"remainingProcesses\":").Append(remaining).Append(",\"desktopClosed\":").Append(closed ? "true" : "false")
         .Append(",\"ok\":true}");
        return o.ToString();
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacroRecorder.Waiting;

internal sealed class WindowsMemoryProcessApi : IMemoryProcessApi
{
    public IMemoryProcess? Bind(string executablePath, CancellationToken token)
    {
        IMemoryProcess? bound = null;
        var candidates = WindowsMemoryProcess.ProcessIds(Path.GetFileName(executablePath), token);
        try
        {
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                var process = WindowsMemoryProcess.Open(candidate);
                if (!string.Equals(process.Identity.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) { process.Dispose(); continue; }
                if (bound is not null) { process.Dispose(); throw new InvalidOperationException("Multiple process instances match the executable path."); }
                bound = process;
            }
            var result = bound; bound = null; return result;
        }
        finally { bound?.Dispose(); }
    }

    internal static ChoiceResult<ProcessChoice> List(Func<bool> permitted, CancellationToken token)
    {
        if (!permitted()) return new(ReadStatus.Error, [], "Enable read-only memory locally to list processes.");
        var candidates = WindowsMemoryProcess.ProcessIds(null, token);
        var choices = new List<ProcessChoice>();
        var denied = 0;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!permitted()) return new(ReadStatus.Error, [], "Memory permission was revoked.", false);
            try { using var process = WindowsMemoryProcess.Open(candidate, read: false); choices.Add(process.Identity); }
            catch (Win32Exception) { denied++; }
        }
        return new(ReadStatus.Success, choices.OrderBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            denied == 0 ? "" : $"{denied} inaccessible or exited processes were omitted; this listing cannot establish absence.", denied == 0);
    }
}

internal sealed class WindowsMemoryProcess : IMemoryProcess
{
    private readonly SafeProcessHandle _handle;
    public ProcessChoice Identity { get; }
    public int PointerSize { get; }

    private WindowsMemoryProcess(SafeProcessHandle handle, ProcessChoice identity, int pointerSize)
    { _handle = handle; Identity = identity; PointerSize = pointerSize; }

    internal static WindowsMemoryProcess Open(uint pid, bool read = true)
    {
        // Only query identity and (for observations) read memory. Never write,
        // request debug privilege, elevate, inject or scan address ranges.
        var handle = OpenProcess(0x00101000u | (read ? 0x10u : 0u), false, pid);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Process access denied or process exited.");
            if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var path = new StringBuilder(32768); var size = (uint)path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!IsWow64Process2(handle, out var machine, out var native)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var target = machine == 0 ? native : machine;
            var pointerSize = target switch { 0x014c => 4, 0x8664 or 0xaa64 => 8, _ => 0 };
            return new(handle, new(pid, created, path.ToString()), pointerSize);
        }
        catch { handle.Dispose(); throw; }
    }

    public bool IsAlive => !_handle.IsClosed && !_handle.IsInvalid
        && WaitForSingleObject(_handle, 0) == 258
        && GetProcessTimes(_handle, out var created, out _, out _, out _) && created == Identity.ProcessCreated;

    internal static IReadOnlyList<uint> ProcessIds(string? executableName, CancellationToken token)
    {
        using var snapshot = CreateToolhelp32Snapshot(0x2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Process enumeration unavailable.");
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var ids = new List<uint>(); var visited = 0;
        do
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 4096) throw new InvalidOperationException("Process enumeration exceeded 4096 entries.");
            if (executableName is null || string.Equals(executableName, entry.ExeName, StringComparison.OrdinalIgnoreCase)) ids.Add(entry.ProcessId);
            entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
        } while (Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error(), "Incomplete process enumeration.");
        return ids;
    }

    public MemoryReadResult Read(ulong address, int count)
    {
        if (count is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(count));
        if (!IsAlive) throw new InvalidOperationException("Bound process has exited.");
        var bytes = new byte[count];
        var ok = ReadProcessMemory(_handle, unchecked((nint)address), bytes, (nuint)count, out var read);
        var error = Marshal.GetLastWin32Error();
        if (!ok && error == 5) throw new Win32Exception(error, "Memory read access denied.");
        return new(ok, bytes, read <= int.MaxValue ? (int)read : 0);
    }

    public IReadOnlyList<ModuleChoice> Modules(CancellationToken token)
    {
        if (!IsAlive) throw new InvalidOperationException("Bound process has exited.");
        using var snapshot = CreateToolhelp32Snapshot(0x8 | 0x10, Identity.ProcessId);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Module enumeration unavailable.");
        var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf<ModuleEntry>() };
        if (!Module32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var modules = new List<ModuleChoice>();
        do
        {
            token.ThrowIfCancellationRequested();
            if (modules.Count >= 1024) throw new InvalidOperationException("Module listing exceeded 1024 entries.");
            var version = FileVersionInfo.GetVersionInfo(entry.ExePath).FileVersion ?? "";
            modules.Add(new(entry.ExePath, version, unchecked((ulong)entry.BaseAddress), entry.BaseSize));
            entry.Size = (uint)Marshal.SizeOf<ModuleEntry>();
        } while (Module32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error(), "Incomplete module enumeration.");
        if (!IsAlive) throw new InvalidOperationException("Bound process exited during module enumeration.");
        return modules;
    }

    public void Dispose() => _handle.Dispose();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry
    {
        public uint Size, ModuleId, ProcessId, GlobalUsage, ProcessUsage;
        public nint BaseAddress;
        public uint BaseSize;
        public nint Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExePath;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort machine, out ushort nativeMachine);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Module32First(SafeFileHandle snapshot, ref ModuleEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Module32Next(SafeFileHandle snapshot, ref ModuleEntry entry);
}

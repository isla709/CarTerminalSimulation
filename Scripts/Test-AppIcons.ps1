param([string]$BuildDirectory = 'TerminalSimulation.Wpf/bin/Debug/net8.0-windows/win-x64')

$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$build = [IO.Path]::GetFullPath((Join-Path $repoDirectory $BuildDirectory))
$expectedSizes = @(16,20,24,32,40,48,64,96,128,256)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class IconProbe {
    private delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("user32.dll", SetLastError=true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    public static int[] ExeSizes(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
        if(module == IntPtr.Zero) throw new InvalidOperationException("Cannot load EXE resources.");
        var sizes = new List<int>();
        try {
            EnumResourceName callback = (m, t, n, d) => {
                var resource = FindResource(m, n, t);
                var bytes = new byte[SizeofResource(m, resource)];
                Marshal.Copy(LockResource(LoadResource(m, resource)), bytes, 0, bytes.Length);
                var count = BitConverter.ToUInt16(bytes, 4);
                for(var i=0;i<count;i++) sizes.Add(bytes[6+i*14] == 0 ? 256 : bytes[6+i*14]);
                return true;
            };
            if(!EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero))
                throw new InvalidOperationException("EXE has no group icon resource.");
            GC.KeepAlive(callback);
            return sizes.ToArray();
        } finally { FreeLibrary(module); }
    }
    public static long WindowIcon(IntPtr window, int kind) {
        IntPtr icon;
        if(SendMessageTimeout(window, 0x7f, new IntPtr(kind), IntPtr.Zero, 2, 2000, out icon) == IntPtr.Zero)
            throw new InvalidOperationException("Window did not answer WM_GETICON.");
        return icon.ToInt64();
    }
}
'@

foreach ($entry in @(
    @{Asset='TerminalSimulation.Wpf/Assets/app-icon.ico'; Exe='TerminalSimulation.Wpf.exe'},
    @{Asset='TerminalSimulation.Updater/Assets/update-icon.ico'; Exe='update.exe'}
)) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $repoDirectory $entry.Asset))
    if ([BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1 -or
        [BitConverter]::ToUInt16($bytes, 4) -ne $expectedSizes.Count) { throw 'Invalid ICO header.' }
    for ($index=0; $index -lt $expectedSizes.Count; $index++) {
        $offset = 6 + $index * 16
        $size = if ($bytes[$offset] -eq 0) { 256 } else { [int]$bytes[$offset] }
        $length = [BitConverter]::ToUInt32($bytes, $offset+8)
        $dataOffset = [BitConverter]::ToUInt32($bytes, $offset+12)
        if ($size -ne $expectedSizes[$index] -or [BitConverter]::ToUInt16($bytes, $offset+6) -ne 32 -or
            $dataOffset + $length -gt $bytes.Length) { throw 'Invalid ICO image entry.' }
    }
    $nativeSizes = [IconProbe]::ExeSizes((Join-Path $build $entry.Exe))
    if (($nativeSizes -join ',') -ne ($expectedSizes -join ',')) { throw "EXE icon sizes mismatch: $($entry.Exe)" }
    Write-Output "EXE icon resource passed: $($entry.Exe) [$($nativeSizes -join ',')]"
}

# Isolate smoke runs from existing user configuration and workspaces.
$qa = Join-Path $repoDirectory ('artifacts/.qa/app-icons-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $qa)
Get-ChildItem -LiteralPath $build -File | Where-Object {
    $_.Extension -in @('.exe','.dll','.json','.dat','.txt') -and
    $_.Name -notin @('config.json','utility_settings.json','update-sources.json')
} | Copy-Item -Destination $qa
foreach ($directory in @('MapApp','ffmpeg-native','Plugins')) {
    $source = Join-Path $build $directory
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $qa -Recurse }
}

foreach ($exe in @('TerminalSimulation.Wpf.exe','update.exe')) {
    $start = @{FilePath=(Join-Path $qa $exe); WorkingDirectory=$qa; WindowStyle='Hidden'; PassThru=$true}
    # Missing plan fails validation before network requests or installation writes.
    if ($exe -eq 'update.exe') { $start.ArgumentList = @('--worker','--plan', ('"' + (Join-Path $qa 'missing-plan.json') + '"')) }
    $process = Start-Process @start
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 300
            $process.Refresh()
            if ($process.HasExited) { throw "$exe exited during startup: $($process.ExitCode)" }
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw "$exe did not create a window." }
        if ($exe -ne 'update.exe') {
            # Allow the splash to finish and verify the main HWND, not the splash.
            Start-Sleep -Seconds 8
            $process.Refresh()
        }
        $large = [IconProbe]::WindowIcon($process.MainWindowHandle, 1)
        $small = [IconProbe]::WindowIcon($process.MainWindowHandle, 0)
        if ($large -eq 0 -or $small -eq 0) { throw "$exe has missing native window icons." }
        if (Test-Path -LiteralPath (Join-Path $qa 'startup-crash.log')) { throw 'Application reported a startup crash.' }
        Write-Output "Window icons passed: $exe; big=$large; small=$small; responding=$($process.Responding)"
    } finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
        }
    }
}
Write-Output "Icon smoke checks passed. Isolated run: $qa"

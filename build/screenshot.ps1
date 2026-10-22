# Captures a running Torrent7 window to a PNG. Used to check the interface during
# development and to produce the screenshots in the README.
#
#   powershell -ExecutionPolicy Bypass -File build\screenshot.ps1 -Out assets\app.png
#   powershell -ExecutionPolicy Bypass -File build\screenshot.ps1 -Title "Add torrent" -Out assets\add.png
#
# PrintWindow reads the window's own surface, so the capture never picks up whatever else
# happens to be on screen and the window does not need to be in front.

param(
    [string]$Out = 'assets\app.png',
    [string]$Title = ''
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Shot {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    // A visible window of the given process with exactly this title.
    public static IntPtr Find(uint pid, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$root = Split-Path -Parent $PSScriptRoot
$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }

if ($Title) {
    $handle = [IntPtr]::Zero
    foreach ($p in Get-Process Torrent7 -ErrorAction SilentlyContinue) {
        $handle = [Shot]::Find([uint32]$p.Id, $Title)
        if ($handle -ne [IntPtr]::Zero) { break }
    }
    if ($handle -eq [IntPtr]::Zero) { throw "No Torrent7 window titled '$Title'." }
} else {
    $proc = Get-Process Torrent7 -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $proc) { throw 'Torrent7 is not running with a visible window.' }
    $handle = $proc.MainWindowHandle
}

# DWMWA_EXTENDED_FRAME_BOUNDS, so the size matches what the user sees.
$rect = New-Object Shot+RECT
[void][Shot]::DwmGetWindowAttribute($handle, 9, [ref]$rect, 16)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# PW_RENDERFULLCONTENT, required for GPU composed surfaces.
$ok = [Shot]::PrintWindow($handle, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not $ok) { $bmp.Dispose(); throw 'PrintWindow failed.' }

New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "shot -> $outPath ($w x $h)"

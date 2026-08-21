// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.InteropServices;

namespace StrideAssetStore.Desktop.Services;

/// <summary>
/// Last-resort escape hatch (Windows): system-wide hotkeys handled on a dedicated thread with its
/// own message loop, so they keep working when the web UI can't be used — a dead Blazor circuit, a
/// crashed page, or a wedged request pipeline. Without them, an app whose console is hidden and
/// whose UI is broken can only be killed from the Task Manager.
/// </summary>
public static class GlobalHotkeys
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;

    private const int IdConsole = 0xA501;
    private const int IdQuit = 0xA502;

    /// <summary>Human-readable description of the registered hotkeys, or null when none is active.</summary>
    public static string? Description { get; private set; }

    /// <summary>
    /// Registers Ctrl+Alt+Shift+C (toggle the console window) and Ctrl+Alt+Shift+Q (quit).
    /// Best-effort: a hotkey already taken by another app simply doesn't register.
    /// </summary>
    public static void Start(Action toggleConsole, Action quit)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            const uint mods = ModControl | ModAlt | ModShift | ModNoRepeat;
            var console = RegisterHotKey(IntPtr.Zero, IdConsole, mods, 0x43 /* C */);
            var stop = RegisterHotKey(IntPtr.Zero, IdQuit, mods, 0x51 /* Q */);
            Description = (console, stop) switch
            {
                (true, true) => "Ctrl+Alt+Shift+C = console, Ctrl+Alt+Shift+Q = quit",
                (true, false) => "Ctrl+Alt+Shift+C = console",
                (false, true) => "Ctrl+Alt+Shift+Q = quit",
                _ => null,
            };
            ready.Set();

            if (!console && !stop)
            {
                return;
            }

            // GetMessage blocks this thread only — WM_HOTKEY is posted to the thread queue
            // because the hotkeys are registered with a null window handle.
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message != WmHotkey)
                {
                    continue;
                }

                try
                {
                    if ((int)message.WParam == IdConsole)
                    {
                        toggleConsole();
                    }
                    else if ((int)message.WParam == IdQuit)
                    {
                        quit();
                    }
                }
                catch
                {
                    // The escape hatch must never take the app down itself.
                }
            }
        })
        {
            IsBackground = true,
            Name = "StrideAssetStore hotkeys",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2)); // so the banner can report what actually registered
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint filterMin, uint filterMax);
}

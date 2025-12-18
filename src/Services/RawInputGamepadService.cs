using System.Runtime.InteropServices;
using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// Gamepad service using Raw Input API - works with DirectInput gamepads (Fantech, generic, etc.)
/// Falls back from XInput to Raw HID for broader compatibility.
/// </summary>
public class RawInputGamepadService : IDisposable
{
    // Raw Input structures
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_HID hid;
        // Union padding for other device types
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    // HID P/Invoke for reading joystick state directly
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(IntPtr hDevice, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000b;
    private const uint RIM_TYPEHID = 2;

    // Joystick reading via winmm.dll (simpler DirectInput alternative)
    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern uint joyGetDevCaps(uint uJoyID, ref JOYCAPS pjc, uint cbjc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct JOYCAPS
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    private const uint JOY_RETURNALL = 0x000000FF;
    private const uint JOY_RETURNPOV = 0x00000040;
    private const uint JOY_RETURNBUTTONS = 0x00000080;
    private const uint JOYERR_NOERROR = 0;

    // POV hat directions
    private const uint JOY_POVCENTERED = 0xFFFF;
    private const uint JOY_POVFORWARD = 0;
    private const uint JOY_POVRIGHT = 9000;
    private const uint JOY_POVBACKWARD = 18000;
    private const uint JOY_POVLEFT = 27000;

    // Simulate Alt+Tab via SendInput
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;    // Alt key
    private const ushort VK_TAB = 0x09;     // Tab key

    // Events
    public event Action<GamepadButton>? OnButtonPressed;
    public event Action<GamepadDirection>? OnDirectionChanged;
    public event Action? OnCloseComboHeld;
    public event Action? OnAltTabRequested; // For LB+RB quick press (Alt+Tab)
    public event Action<uint>? OnRawButtonPressed; // For controller mapping mode

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private uint _lastButtons;
    private GamepadDirection _lastDirection = GamepadDirection.None;
    private uint _connectedJoystickId = uint.MaxValue;

    // Repeat navigation settings
    private DateTime _lastNavigationTime = DateTime.MinValue;
    private readonly TimeSpan _navigationRepeatDelay = TimeSpan.FromMilliseconds(150);
    private readonly TimeSpan _initialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private bool _isHoldingDirection;

    // Close combo detection (LB+RB+Start for 2 seconds)
    private DateTime? _closeComboStartTime = null;
    private const int CLOSE_COMBO_HOLD_MS = 2000;

    // Alt+Tab combo detection (LB+RB quick tap without Start)
    private DateTime? _altTabComboStartTime = null;
    private bool _altTabFired = false;
    private const int ALT_TAB_QUICK_TAP_MS = 500; // Must release within 500ms for Alt+Tab

    // Configurable button mapping (loaded from config)
    private uint _buttonA;
    private uint _buttonB;
    private uint _buttonX;
    private uint _buttonY;
    private uint _buttonLB;
    private uint _buttonRB;
    private uint _buttonBack;
    private uint _buttonStart;
    private uint _buttonLStick;
    private uint _buttonRStick;

    public RawInputGamepadService(ControllerConfig? config = null)
    {
        // Load button mapping from config or use defaults
        if (config != null)
        {
            UpdateButtonMapping(config);
        }
        else
        {
            // Default button mapping for typical DirectInput gamepad
            _buttonA = 0x0002;      // Button 2 (often A/Cross)
            _buttonB = 0x0004;      // Button 3 (often B/Circle)
            _buttonX = 0x0001;      // Button 1 (often X/Square)
            _buttonY = 0x0008;      // Button 4 (often Y/Triangle)
            _buttonLB = 0x0010;     // Button 5 (Left Bumper)
            _buttonRB = 0x0020;     // Button 6 (Right Bumper)
            _buttonBack = 0x0040;   // Button 7 (Back/Select)
            _buttonStart = 0x0080;  // Button 8 (Start)
            _buttonLStick = 0x0100; // Button 9 (Left Stick Click)
            _buttonRStick = 0x0200; // Button 10 (Right Stick Click)
        }
    }

    public void UpdateButtonMapping(ControllerConfig config)
    {
        _buttonA = config.ButtonA;
        _buttonB = config.ButtonB;
        _buttonX = config.ButtonX;
        _buttonY = config.ButtonY;
        _buttonLB = config.ButtonLB;
        _buttonRB = config.ButtonRB;
        _buttonBack = config.ButtonBack;
        _buttonStart = config.ButtonStart;
        _buttonLStick = config.ButtonLStick;
        _buttonRStick = config.ButtonRStick;

        System.Diagnostics.Debug.WriteLine($"Button mapping updated: A={_buttonA:X}, B={_buttonB:X}, Start={_buttonStart:X}");
    }

    public void StartPolling()
    {
        // Find connected joystick
        FindConnectedJoystick();

        if (_connectedJoystickId == uint.MaxValue)
        {
            System.Diagnostics.Debug.WriteLine("No DirectInput joystick found - gamepad support disabled");
            return;
        }

        _cts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollLoop(_cts.Token));
    }

    private void FindConnectedJoystick()
    {
        uint numDevices = joyGetNumDevs();
        System.Diagnostics.Debug.WriteLine($"joyGetNumDevs reports {numDevices} possible joystick slots");

        for (uint i = 0; i < numDevices && i < 16; i++)
        {
            var caps = new JOYCAPS();
            uint result = joyGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<JOYCAPS>());

            if (result == JOYERR_NOERROR)
            {
                System.Diagnostics.Debug.WriteLine($"Joystick {i} found: {caps.szPname} (Buttons: {caps.wNumButtons})");

                // Try to read position to confirm it's actually connected
                var info = new JOYINFOEX
                {
                    dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                    dwFlags = JOY_RETURNALL
                };

                if (joyGetPosEx(i, ref info) == JOYERR_NOERROR)
                {
                    _connectedJoystickId = i;
                    System.Diagnostics.Debug.WriteLine($"Using joystick {i}: {caps.szPname}");
                    return;
                }
            }
        }
    }

    public void StopPolling()
    {
        _cts?.Cancel();
        try
        {
            _pollingTask?.Wait(1000);
        }
        catch { }
    }

    private void PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_connectedJoystickId != uint.MaxValue)
                {
                    var info = new JOYINFOEX
                    {
                        dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                        dwFlags = JOY_RETURNALL | JOY_RETURNPOV | JOY_RETURNBUTTONS
                    };

                    uint result = joyGetPosEx(_connectedJoystickId, ref info);
                    if (result == JOYERR_NOERROR)
                    {
                        ProcessJoystickState(info);
                    }
                    else
                    {
                        // Joystick disconnected, try to find another
                        _connectedJoystickId = uint.MaxValue;
                        FindConnectedJoystick();
                    }
                }
                else
                {
                    // Periodically check for new joysticks
                    FindConnectedJoystick();
                }

                Thread.Sleep(16); // ~60Hz polling
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Joystick poll error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessJoystickState(JOYINFOEX info)
    {
        // Process buttons
        ProcessButtons(info.dwButtons);

        // Process direction (D-pad via POV hat + left stick)
        ProcessDirection(info);

        // Check close combo (LB + RB + Start for 3s)
        CheckCloseCombo(info.dwButtons);

        // Check Alt+Tab combo (LB + RB quick tap without Start)
        CheckAltTabCombo(info.dwButtons);
    }

    private void ProcessButtons(uint buttons)
    {
        // Detect new button presses
        uint newPresses = buttons & ~_lastButtons;
        _lastButtons = buttons;

        // Fire raw button event for mapping mode (any new press)
        if (newPresses != 0)
        {
            // Find the first pressed button for mapping
            for (int i = 0; i < 16; i++)
            {
                uint mask = 1u << i;
                if ((newPresses & mask) != 0)
                {
                    OnRawButtonPressed?.Invoke(mask);
                    break; // Only report first button for mapping
                }
            }
        }

        // Map buttons using configurable mapping
        if ((newPresses & _buttonA) != 0) OnButtonPressed?.Invoke(GamepadButton.A);
        if ((newPresses & _buttonB) != 0) OnButtonPressed?.Invoke(GamepadButton.B);
        if ((newPresses & _buttonX) != 0) OnButtonPressed?.Invoke(GamepadButton.X);
        if ((newPresses & _buttonY) != 0) OnButtonPressed?.Invoke(GamepadButton.Y);
        if ((newPresses & _buttonStart) != 0) OnButtonPressed?.Invoke(GamepadButton.Start);
        if ((newPresses & _buttonBack) != 0) OnButtonPressed?.Invoke(GamepadButton.Back);
        if ((newPresses & _buttonLB) != 0) OnButtonPressed?.Invoke(GamepadButton.LeftBumper);
        if ((newPresses & _buttonRB) != 0) OnButtonPressed?.Invoke(GamepadButton.RightBumper);
    }

    private void ProcessDirection(JOYINFOEX info)
    {
        GamepadDirection direction = GamepadDirection.None;

        // POV hat (D-pad) - takes priority
        if (info.dwPOV != JOY_POVCENTERED && info.dwPOV != 0xFFFFFFFF)
        {
            // POV is in hundredths of degrees, 0 = up, 9000 = right, etc.
            uint pov = info.dwPOV;
            if (pov >= 31500 || pov < 4500) direction = GamepadDirection.Up;
            else if (pov >= 4500 && pov < 13500) direction = GamepadDirection.Right;
            else if (pov >= 13500 && pov < 22500) direction = GamepadDirection.Down;
            else if (pov >= 22500 && pov < 31500) direction = GamepadDirection.Left;
        }

        // Fall back to left stick if no D-pad input
        // Axes are typically 0-65535, with center at 32767
        if (direction == GamepadDirection.None)
        {
            const uint CENTER = 32767;
            const uint DEADZONE = 12000;

            int xOffset = (int)info.dwXpos - (int)CENTER;
            int yOffset = (int)info.dwYpos - (int)CENTER;

            if (yOffset < -(int)DEADZONE) direction = GamepadDirection.Up;
            else if (yOffset > (int)DEADZONE) direction = GamepadDirection.Down;
            else if (xOffset < -(int)DEADZONE) direction = GamepadDirection.Left;
            else if (xOffset > (int)DEADZONE) direction = GamepadDirection.Right;
        }

        // Handle direction with repeat
        if (direction != GamepadDirection.None)
        {
            var now = DateTime.Now;
            var delay = _isHoldingDirection ? _navigationRepeatDelay : _initialRepeatDelay;

            if (direction != _lastDirection || now - _lastNavigationTime > delay)
            {
                OnDirectionChanged?.Invoke(direction);
                _lastNavigationTime = now;
                _isHoldingDirection = (direction == _lastDirection);
            }
        }
        else
        {
            _isHoldingDirection = false;
        }

        _lastDirection = direction;
    }

    private void CheckCloseCombo(uint buttons)
    {
        // LB + RB + Start (using configured buttons)
        bool isComboPressed =
            (buttons & _buttonLB) != 0 &&
            (buttons & _buttonRB) != 0 &&
            (buttons & _buttonStart) != 0;

        if (isComboPressed)
        {
            if (_closeComboStartTime == null)
            {
                _closeComboStartTime = DateTime.Now;
            }
            else if ((DateTime.Now - _closeComboStartTime.Value).TotalMilliseconds >= CLOSE_COMBO_HOLD_MS)
            {
                OnCloseComboHeld?.Invoke();
                _closeComboStartTime = null;
            }
        }
        else
        {
            _closeComboStartTime = null;
        }
    }

    private void CheckAltTabCombo(uint buttons)
    {
        // LB + RB pressed (without Start) - for Alt+Tab
        bool lbPressed = (buttons & _buttonLB) != 0;
        bool rbPressed = (buttons & _buttonRB) != 0;
        bool startPressed = (buttons & _buttonStart) != 0;
        bool isAltTabCombo = lbPressed && rbPressed && !startPressed;

        if (isAltTabCombo)
        {
            if (_altTabComboStartTime == null)
            {
                _altTabComboStartTime = DateTime.Now;
                _altTabFired = false;
            }
            else if (!_altTabFired && (DateTime.Now - _altTabComboStartTime.Value).TotalMilliseconds < ALT_TAB_QUICK_TAP_MS)
            {
                // Still holding, wait for release or timeout
            }
        }
        else if (_altTabComboStartTime != null)
        {
            // Combo released - check if it was a quick tap
            var holdTime = (DateTime.Now - _altTabComboStartTime.Value).TotalMilliseconds;

            if (!_altTabFired && holdTime < ALT_TAB_QUICK_TAP_MS)
            {
                // Quick tap detected - fire Alt+Tab
                SimulateAltTab();
                OnAltTabRequested?.Invoke();
                _altTabFired = true;
            }

            _altTabComboStartTime = null;
        }
    }

    private void SimulateAltTab()
    {
        var inputs = new INPUT[4];

        // Alt key down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_MENU;
        inputs[0].u.ki.dwFlags = 0;

        // Tab key down
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_TAB;
        inputs[1].u.ki.dwFlags = 0;

        // Tab key up
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_TAB;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

        // Alt key up
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_MENU;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        System.Diagnostics.Debug.WriteLine("Alt+Tab simulated via gamepad LB+RB");
    }

    public bool IsConnected => _connectedJoystickId != uint.MaxValue;

    public void Dispose()
    {
        StopPolling();
        _cts?.Dispose();
    }
}

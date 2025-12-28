using System.Runtime.InteropServices;
using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// Gamepad service using DirectInput API via winmm.dll
/// Supports generic controllers (Fantech, etc.) and PlayStation controllers (DS4, DualSense)
/// </summary>
public class DirectInputGamepadService : IGamepadService
{
    // Raw Input structures for device info
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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] padding;
    }

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

    // Joystick reading via winmm.dll
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
        public ushort wMid;   // Manufacturer ID (Vendor ID)
        public ushort wPid;   // Product ID
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

    // Input simulation
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
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

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_TAB = 0x09;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    // PlayStation controller detection
    private const ushort SONY_VENDOR_ID = 0x054C;
    private static readonly HashSet<ushort> PlayStationProductIds = new()
    {
        0x05C4, // DualShock 4 v1
        0x09CC, // DualShock 4 v2
        0x0CE6, // DualSense
        0x0DF2  // DualSense Edge
    };

    // Events
    public event Action<GamepadButton>? OnButtonPressed;
    public event Action<GamepadDirection>? OnDirectionChanged;
    public event Action? OnCloseComboHeld;
    public event Action? OnAltTabRequested;
    public event Action<uint>? OnRawButtonPressed;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private uint _lastButtons;
    private GamepadDirection _lastDirection = GamepadDirection.None;
    private uint _connectedJoystickId = uint.MaxValue;
    private GamepadType _controllerType = GamepadType.DirectInput;

    // Repeat navigation settings
    private DateTime _lastNavigationTime = DateTime.MinValue;
    private readonly TimeSpan _navigationRepeatDelay = TimeSpan.FromMilliseconds(150);
    private readonly TimeSpan _initialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private bool _isHoldingDirection;

    // Close combo detection (LB+RB+Start for 2 seconds)
    private DateTime? _closeComboStartTime = null;
    private const int CLOSE_COMBO_HOLD_MS = 2000;

    // Alt+Tab combo detection
    private DateTime? _altTabComboStartTime = null;
    private bool _altTabFired = false;
    private const int ALT_TAB_QUICK_TAP_MS = 500;

    // Mouse control settings (right stick)
    private const uint MOUSE_DEADZONE = 8000;
    private const float MOUSE_SPEED_MAX = 20.0f;
    private const float MOUSE_SPEED_MIN = 2.0f;
    private DateTime _lastRightStickActivity = DateTime.MinValue;
    private const int MOUSE_MODE_TIMEOUT_MS = 1000;

    // Configurable button mapping
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

    // Flag to skip XInput devices
    private bool _skipXInputDevices = false;

    public bool IsConnected => _connectedJoystickId != uint.MaxValue;
    public GamepadType ControllerType => _controllerType;

    public DirectInputGamepadService(ControllerConfig? config = null, bool skipXInputDevices = false)
    {
        _skipXInputDevices = skipXInputDevices;

        if (config != null)
        {
            UpdateButtonMapping(config);
        }
        else
        {
            // Default button mapping for typical DirectInput gamepad
            _buttonA = 0x0002;
            _buttonB = 0x0004;
            _buttonX = 0x0001;
            _buttonY = 0x0008;
            _buttonLB = 0x0010;
            _buttonRB = 0x0020;
            _buttonBack = 0x0040;
            _buttonStart = 0x0080;
            _buttonLStick = 0x0100;
            _buttonRStick = 0x0200;
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

        System.Diagnostics.Debug.WriteLine($"DirectInput button mapping updated: A={_buttonA:X}, B={_buttonB:X}, Start={_buttonStart:X}");
    }

    /// <summary>
    /// Check if a DirectInput controller is available
    /// </summary>
    public bool TryConnect()
    {
        FindConnectedJoystick();
        return _connectedJoystickId != uint.MaxValue;
    }

    public void StartPolling()
    {
        FindConnectedJoystick();

        if (_connectedJoystickId == uint.MaxValue)
        {
            System.Diagnostics.Debug.WriteLine("No DirectInput joystick found - service disabled");
            return;
        }

        _cts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollLoop(_cts.Token));
    }

    private void FindConnectedJoystick()
    {
        uint numDevices = joyGetNumDevs();
        System.Diagnostics.Debug.WriteLine($"DirectInput: joyGetNumDevs reports {numDevices} possible joystick slots");

        for (uint i = 0; i < numDevices && i < 16; i++)
        {
            var caps = new JOYCAPS();
            uint result = joyGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<JOYCAPS>());

            if (result == JOYERR_NOERROR)
            {
                // Check if this is an XInput device (should be skipped if XInput service is active)
                if (_skipXInputDevices && IsXInputDevice(caps.szPname))
                {
                    System.Diagnostics.Debug.WriteLine($"DirectInput: Skipping XInput device {i}: {caps.szPname}");
                    continue;
                }

                // Try to read position to confirm it's actually connected
                var info = new JOYINFOEX
                {
                    dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                    dwFlags = JOY_RETURNALL
                };

                if (joyGetPosEx(i, ref info) == JOYERR_NOERROR)
                {
                    _connectedJoystickId = i;

                    // Detect controller type
                    _controllerType = DetectControllerType(caps.wMid, caps.wPid, caps.szPname);

                    System.Diagnostics.Debug.WriteLine($"DirectInput: Using joystick {i}: {caps.szPname} (VID:{caps.wMid:X4} PID:{caps.wPid:X4}) - Type: {_controllerType}");
                    return;
                }
            }
        }

        _connectedJoystickId = uint.MaxValue;
        _controllerType = GamepadType.None;
    }

    /// <summary>
    /// Check if device name indicates an XInput device (Xbox controller)
    /// </summary>
    private bool IsXInputDevice(string deviceName)
    {
        // Xbox controllers show up with "IG_" in the device path when accessed via DirectInput
        // Also check for common Xbox controller names
        if (string.IsNullOrEmpty(deviceName)) return false;

        var nameLower = deviceName.ToLowerInvariant();
        return nameLower.Contains("ig_") ||
               nameLower.Contains("xbox") ||
               nameLower.Contains("xinput");
    }

    /// <summary>
    /// Detect if controller is PlayStation based on VID/PID
    /// </summary>
    private GamepadType DetectControllerType(ushort vendorId, ushort productId, string deviceName)
    {
        // Check for Sony PlayStation controllers
        if (vendorId == SONY_VENDOR_ID)
        {
            if (PlayStationProductIds.Contains(productId))
            {
                System.Diagnostics.Debug.WriteLine($"DirectInput: Detected PlayStation controller (PID: {productId:X4})");
                return GamepadType.PlayStation;
            }
        }

        // Also check device name for PlayStation indicators
        if (!string.IsNullOrEmpty(deviceName))
        {
            var nameLower = deviceName.ToLowerInvariant();
            if (nameLower.Contains("dualshock") ||
                nameLower.Contains("dualsense") ||
                nameLower.Contains("wireless controller") ||
                nameLower.Contains("sony"))
            {
                return GamepadType.PlayStation;
            }
        }

        return GamepadType.DirectInput;
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
                        _connectedJoystickId = uint.MaxValue;
                        _controllerType = GamepadType.None;
                        FindConnectedJoystick();
                    }
                }
                else
                {
                    FindConnectedJoystick();
                }

                Thread.Sleep(16); // ~60Hz polling
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DirectInput poll error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessJoystickState(JOYINFOEX info)
    {
        ProcessButtons(info.dwButtons);
        ProcessDirection(info);
        ProcessRightStickMouse(info);
        CheckCloseCombo(info.dwButtons);
        CheckAltTabCombo(info.dwButtons);
    }

    private void ProcessButtons(uint buttons)
    {
        uint newPresses = buttons & ~_lastButtons;
        _lastButtons = buttons;

        if (newPresses != 0)
        {
            for (int i = 0; i < 16; i++)
            {
                uint mask = 1u << i;
                if ((newPresses & mask) != 0)
                {
                    OnRawButtonPressed?.Invoke(mask);
                    break;
                }
            }
        }

        if ((newPresses & _buttonA) != 0)
        {
            if (IsInMouseMode())
            {
                SimulateMouseClick();
            }
            else
            {
                OnButtonPressed?.Invoke(GamepadButton.A);
            }
        }
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

        if (info.dwPOV != JOY_POVCENTERED && info.dwPOV != 0xFFFFFFFF)
        {
            uint pov = info.dwPOV;
            if (pov >= 31500 || pov < 4500) direction = GamepadDirection.Up;
            else if (pov >= 4500 && pov < 13500) direction = GamepadDirection.Right;
            else if (pov >= 13500 && pov < 22500) direction = GamepadDirection.Down;
            else if (pov >= 22500 && pov < 31500) direction = GamepadDirection.Left;
        }

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
        }
        else if (_altTabComboStartTime != null)
        {
            var holdTime = (DateTime.Now - _altTabComboStartTime.Value).TotalMilliseconds;

            if (!_altTabFired && holdTime < ALT_TAB_QUICK_TAP_MS)
            {
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

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_MENU;
        inputs[0].u.ki.dwFlags = 0;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_TAB;
        inputs[1].u.ki.dwFlags = 0;

        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_TAB;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_MENU;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        System.Diagnostics.Debug.WriteLine("DirectInput: Alt+Tab simulated via LB+RB");
    }

    private bool IsInMouseMode()
    {
        return (DateTime.Now - _lastRightStickActivity).TotalMilliseconds < MOUSE_MODE_TIMEOUT_MS;
    }

    private void ProcessRightStickMouse(JOYINFOEX info)
    {
        const uint CENTER = 32767;

        int xOffset = (int)info.dwZpos - (int)CENTER;
        int yOffset = (int)info.dwRpos - (int)CENTER;

        if (Math.Abs(xOffset) < MOUSE_DEADZONE) xOffset = 0;
        if (Math.Abs(yOffset) < MOUSE_DEADZONE) yOffset = 0;

        if (xOffset != 0 || yOffset != 0)
        {
            _lastRightStickActivity = DateTime.Now;
        }

        if (xOffset == 0 && yOffset == 0) return;

        float magnitude = (float)Math.Sqrt(xOffset * xOffset + yOffset * yOffset);
        float maxMagnitude = 32767 - MOUSE_DEADZONE;
        float speedFactor = Math.Min(magnitude / maxMagnitude, 1.0f);
        float speed = MOUSE_SPEED_MIN + (MOUSE_SPEED_MAX - MOUSE_SPEED_MIN) * speedFactor;

        float normalizedX = xOffset / magnitude;
        float normalizedY = yOffset / magnitude;
        int moveX = (int)(normalizedX * speed * speedFactor);
        int moveY = (int)(normalizedY * speed * speedFactor);

        if (moveX == 0 && moveY == 0) return;

        if (GetCursorPos(out POINT currentPos))
        {
            SetCursorPos(currentPos.X + moveX, currentPos.Y + moveY);
        }
    }

    private void SimulateMouseClick()
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        System.Diagnostics.Debug.WriteLine("DirectInput: Mouse click simulated via A button");
    }

    public void ResetNavigationState()
    {
        _lastDirection = GamepadDirection.None;
        _lastNavigationTime = DateTime.MinValue;
        _isHoldingDirection = false;
        System.Diagnostics.Debug.WriteLine("DirectInput: Navigation state reset");
    }

    public void Dispose()
    {
        StopPolling();
        _cts?.Dispose();
    }
}

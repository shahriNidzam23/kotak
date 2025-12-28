using System.Runtime.InteropServices;
using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// Gamepad service using XInput API for Xbox controllers (360, One, Series X|S)
/// </summary>
public class XInputGamepadService : IGamepadService
{
    // XInput P/Invoke - try multiple DLL versions for compatibility
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState14(int dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState910(int dwUserIndex, ref XINPUT_STATE pState);

    private delegate int XInputGetStateDelegate(int dwUserIndex, ref XINPUT_STATE pState);
    private static XInputGetStateDelegate? _xInputGetState;
    private static bool _xinputInitialized = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    // Mouse control via user32.dll
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
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

    // Input type constants
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;    // Alt key
    private const ushort VK_TAB = 0x09;     // Tab key

    // XInput button flags (standard, fixed values)
    private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    private const ushort XINPUT_GAMEPAD_START = 0x0010;
    private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
    private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
    private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
    private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    private const ushort XINPUT_GAMEPAD_A = 0x1000;
    private const ushort XINPUT_GAMEPAD_B = 0x2000;
    private const ushort XINPUT_GAMEPAD_X = 0x4000;
    private const ushort XINPUT_GAMEPAD_Y = 0x8000;

    // Thumbstick dead zone
    private const short THUMBSTICK_DEADZONE = 8000;

    // Mouse control settings (right stick)
    private const short MOUSE_DEADZONE = 8000;
    private const float MOUSE_SPEED_MAX = 20.0f;
    private const float MOUSE_SPEED_MIN = 2.0f;
    private DateTime _lastRightStickActivity = DateTime.MinValue;
    private const int MOUSE_MODE_TIMEOUT_MS = 1000;

    // Events
    public event Action<GamepadButton>? OnButtonPressed;
    public event Action<GamepadDirection>? OnDirectionChanged;
    public event Action? OnCloseComboHeld;
    public event Action? OnAltTabRequested;
    public event Action<uint>? OnRawButtonPressed;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private ushort _lastButtons;
    private GamepadDirection _lastDirection = GamepadDirection.None;
    private bool _isConnected = false;

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
    private const int ALT_TAB_QUICK_TAP_MS = 500;

    // Customizable button mapping (for user remapping)
    private ushort _buttonA = XINPUT_GAMEPAD_A;
    private ushort _buttonB = XINPUT_GAMEPAD_B;
    private ushort _buttonX = XINPUT_GAMEPAD_X;
    private ushort _buttonY = XINPUT_GAMEPAD_Y;
    private ushort _buttonLB = XINPUT_GAMEPAD_LEFT_SHOULDER;
    private ushort _buttonRB = XINPUT_GAMEPAD_RIGHT_SHOULDER;
    private ushort _buttonBack = XINPUT_GAMEPAD_BACK;
    private ushort _buttonStart = XINPUT_GAMEPAD_START;

    public bool IsConnected => _isConnected;
    public GamepadType ControllerType => GamepadType.XInput;

    static XInputGamepadService()
    {
        InitializeXInput();
    }

    private static void InitializeXInput()
    {
        if (_xinputInitialized) return;
        _xinputInitialized = true;

        // Try xinput1_4.dll first (Windows 8+)
        try
        {
            var testState = new XINPUT_STATE();
            XInputGetState14(0, ref testState);
            _xInputGetState = XInputGetState14;
            System.Diagnostics.Debug.WriteLine("XInput: Using xinput1_4.dll");
            return;
        }
        catch { }

        // Fall back to xinput9_1_0.dll (legacy)
        try
        {
            var testState = new XINPUT_STATE();
            XInputGetState910(0, ref testState);
            _xInputGetState = XInputGetState910;
            System.Diagnostics.Debug.WriteLine("XInput: Using xinput9_1_0.dll");
            return;
        }
        catch { }

        System.Diagnostics.Debug.WriteLine("XInput: No XInput DLL available");
    }

    public XInputGamepadService(ControllerConfig? config = null)
    {
        if (config != null)
        {
            UpdateButtonMapping(config);
        }
    }

    public void UpdateButtonMapping(ControllerConfig config)
    {
        // Map config values to XInput button flags
        // Config stores button indices, we need to map to XInput flags
        _buttonA = MapConfigToXInput(config.ButtonA, XINPUT_GAMEPAD_A);
        _buttonB = MapConfigToXInput(config.ButtonB, XINPUT_GAMEPAD_B);
        _buttonX = MapConfigToXInput(config.ButtonX, XINPUT_GAMEPAD_X);
        _buttonY = MapConfigToXInput(config.ButtonY, XINPUT_GAMEPAD_Y);
        _buttonLB = MapConfigToXInput(config.ButtonLB, XINPUT_GAMEPAD_LEFT_SHOULDER);
        _buttonRB = MapConfigToXInput(config.ButtonRB, XINPUT_GAMEPAD_RIGHT_SHOULDER);
        _buttonBack = MapConfigToXInput(config.ButtonBack, XINPUT_GAMEPAD_BACK);
        _buttonStart = MapConfigToXInput(config.ButtonStart, XINPUT_GAMEPAD_START);

        System.Diagnostics.Debug.WriteLine($"XInput button mapping updated");
    }

    private ushort MapConfigToXInput(uint configValue, ushort defaultValue)
    {
        // If config value matches known XInput flags, use it directly
        // Otherwise use default
        return configValue switch
        {
            0x1000 => XINPUT_GAMEPAD_A,
            0x2000 => XINPUT_GAMEPAD_B,
            0x4000 => XINPUT_GAMEPAD_X,
            0x8000 => XINPUT_GAMEPAD_Y,
            0x0100 => XINPUT_GAMEPAD_LEFT_SHOULDER,
            0x0200 => XINPUT_GAMEPAD_RIGHT_SHOULDER,
            0x0020 => XINPUT_GAMEPAD_BACK,
            0x0010 => XINPUT_GAMEPAD_START,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Check if an XInput controller is available
    /// </summary>
    public bool TryConnect()
    {
        if (_xInputGetState == null) return false;

        // Check all 4 possible controller slots
        for (int i = 0; i < 4; i++)
        {
            var state = new XINPUT_STATE();
            int result = _xInputGetState(i, ref state);
            if (result == 0) // SUCCESS
            {
                _isConnected = true;
                System.Diagnostics.Debug.WriteLine($"XInput controller found at index {i}");
                return true;
            }
        }

        _isConnected = false;
        return false;
    }

    public void StartPolling()
    {
        if (_xInputGetState == null)
        {
            System.Diagnostics.Debug.WriteLine("XInput not available - service disabled");
            return;
        }

        _cts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollLoop(_cts.Token));
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
        int lastConnectedIndex = -1;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool foundController = false;

                // Check all 4 controller slots
                for (int i = 0; i < 4; i++)
                {
                    var state = new XINPUT_STATE();
                    int result = _xInputGetState!(i, ref state);

                    if (result == 0) // SUCCESS
                    {
                        foundController = true;
                        lastConnectedIndex = i;
                        _isConnected = true;
                        ProcessGamepadState(state);
                        break; // Use first connected controller
                    }
                }

                if (!foundController)
                {
                    _isConnected = false;
                    lastConnectedIndex = -1;
                }

                Thread.Sleep(16); // ~60Hz polling
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XInput poll error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessGamepadState(XINPUT_STATE state)
    {
        // Process buttons
        ProcessButtons(state.Gamepad.wButtons);

        // Process direction (D-pad + left stick)
        ProcessDirection(state.Gamepad);

        // Process right stick for mouse control
        ProcessRightStickMouse(state.Gamepad);

        // Check close combo (LB + RB + Start for 2s)
        CheckCloseCombo(state.Gamepad.wButtons);

        // Check Alt+Tab combo (LB + RB quick tap without Start)
        CheckAltTabCombo(state.Gamepad.wButtons);
    }

    private void ProcessButtons(ushort buttons)
    {
        // Detect new button presses
        ushort newPresses = (ushort)(buttons & ~_lastButtons);
        _lastButtons = buttons;

        // Fire raw button event for mapping mode
        if (newPresses != 0)
        {
            OnRawButtonPressed?.Invoke(newPresses);
        }

        // A button: mouse click when in mouse mode, otherwise normal select
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

    private void ProcessDirection(XINPUT_GAMEPAD gamepad)
    {
        GamepadDirection direction = GamepadDirection.None;

        // D-Pad takes priority
        if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_UP) != 0) direction = GamepadDirection.Up;
        else if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_DOWN) != 0) direction = GamepadDirection.Down;
        else if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_LEFT) != 0) direction = GamepadDirection.Left;
        else if ((gamepad.wButtons & XINPUT_GAMEPAD_DPAD_RIGHT) != 0) direction = GamepadDirection.Right;

        // Fall back to left thumbstick
        if (direction == GamepadDirection.None)
        {
            if (gamepad.sThumbLY > THUMBSTICK_DEADZONE) direction = GamepadDirection.Up;
            else if (gamepad.sThumbLY < -THUMBSTICK_DEADZONE) direction = GamepadDirection.Down;
            else if (gamepad.sThumbLX < -THUMBSTICK_DEADZONE) direction = GamepadDirection.Left;
            else if (gamepad.sThumbLX > THUMBSTICK_DEADZONE) direction = GamepadDirection.Right;
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

    private void ProcessRightStickMouse(XINPUT_GAMEPAD gamepad)
    {
        int xOffset = gamepad.sThumbRX;
        int yOffset = -gamepad.sThumbRY; // Invert Y for screen coordinates

        // Apply deadzone
        if (Math.Abs(xOffset) < MOUSE_DEADZONE) xOffset = 0;
        if (Math.Abs(yOffset) < MOUSE_DEADZONE) yOffset = 0;

        // Track activity for mouse mode
        if (xOffset != 0 || yOffset != 0)
        {
            _lastRightStickActivity = DateTime.Now;
        }

        if (xOffset == 0 && yOffset == 0) return;

        // Calculate movement with variable speed based on deflection
        float magnitude = (float)Math.Sqrt(xOffset * xOffset + yOffset * yOffset);
        float maxMagnitude = 32767 - MOUSE_DEADZONE;
        float speedFactor = Math.Min(magnitude / maxMagnitude, 1.0f);
        float speed = MOUSE_SPEED_MIN + (MOUSE_SPEED_MAX - MOUSE_SPEED_MIN) * speedFactor;

        // Normalize and apply speed
        float normalizedX = xOffset / magnitude;
        float normalizedY = yOffset / magnitude;
        int moveX = (int)(normalizedX * speed * speedFactor);
        int moveY = (int)(normalizedY * speed * speedFactor);

        if (moveX == 0 && moveY == 0) return;

        // Move cursor
        if (GetCursorPos(out POINT currentPos))
        {
            SetCursorPos(currentPos.X + moveX, currentPos.Y + moveY);
        }
    }

    private bool IsInMouseMode()
    {
        return (DateTime.Now - _lastRightStickActivity).TotalMilliseconds < MOUSE_MODE_TIMEOUT_MS;
    }

    private void SimulateMouseClick()
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;

        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        System.Diagnostics.Debug.WriteLine("XInput: Mouse click simulated via A button");
    }

    private void CheckCloseCombo(ushort buttons)
    {
        bool isComboPressed =
            (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
            (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 &&
            (buttons & XINPUT_GAMEPAD_START) != 0;

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

    private void CheckAltTabCombo(ushort buttons)
    {
        bool lbPressed = (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
        bool rbPressed = (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
        bool startPressed = (buttons & XINPUT_GAMEPAD_START) != 0;
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
        System.Diagnostics.Debug.WriteLine("XInput: Alt+Tab simulated via LB+RB");
    }

    public void ResetNavigationState()
    {
        _lastDirection = GamepadDirection.None;
        _lastNavigationTime = DateTime.MinValue;
        _isHoldingDirection = false;
        System.Diagnostics.Debug.WriteLine("XInput: Navigation state reset");
    }

    public void Dispose()
    {
        StopPolling();
        _cts?.Dispose();
    }
}

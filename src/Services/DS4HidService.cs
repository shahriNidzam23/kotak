using System.Diagnostics;
using System.Runtime.InteropServices;
using HidSharp;
using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// DualShock 4 / DualSense gamepad service using HidSharp for direct HID access.
/// Provides reliable detection and touchpad support that DirectInput lacks.
/// </summary>
public class DS4HidService : IGamepadService
{
    // Sony Vendor ID
    private const int SONY_VID = 0x054C;

    // PlayStation Product IDs
    private const int DS4_V1_PID = 0x05C4;      // DualShock 4 v1
    private const int DS4_V2_PID = 0x09CC;      // DualShock 4 v2
    private const int DUALSENSE_PID = 0x0CE6;   // DualSense
    private const int DUALSENSE_EDGE_PID = 0x0DF2; // DualSense Edge

    // DS4 HID Report button masks (USB mode)
    private const byte DPAD_MASK = 0x0F;
    private const byte BUTTON_SQUARE = 0x10;
    private const byte BUTTON_CROSS = 0x20;
    private const byte BUTTON_CIRCLE = 0x40;
    private const byte BUTTON_TRIANGLE = 0x80;
    private const byte BUTTON_L1 = 0x01;
    private const byte BUTTON_R1 = 0x02;
    private const byte BUTTON_L2 = 0x04;
    private const byte BUTTON_R2 = 0x08;
    private const byte BUTTON_SHARE = 0x10;
    private const byte BUTTON_OPTIONS = 0x20;
    private const byte BUTTON_L3 = 0x40;
    private const byte BUTTON_R3 = 0x80;
    private const byte BUTTON_PS = 0x01;
    private const byte BUTTON_TOUCHPAD = 0x02;

    // Input simulation P/Invoke
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_TAB = 0x09;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    // Events
    public event Action<GamepadButton>? OnButtonPressed;
    public event Action<GamepadDirection>? OnDirectionChanged;
    public event Action? OnCloseComboHeld;
    public event Action? OnAltTabRequested;
    public event Action<uint>? OnRawButtonPressed;

    // HID device
    private HidDevice? _device;
    private HidStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private bool _isBluetooth;

    // State tracking
    private byte _lastButtons1;
    private byte _lastButtons2;
    private byte _lastButtons3;
    private GamepadDirection _lastDirection = GamepadDirection.None;

    // Repeat navigation settings
    private DateTime _lastNavigationTime = DateTime.MinValue;
    private readonly TimeSpan _navigationRepeatDelay = TimeSpan.FromMilliseconds(150);
    private readonly TimeSpan _initialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private bool _isHoldingDirection;

    // Close combo detection (L1+R1+Options for 2 seconds)
    private DateTime? _closeComboStartTime = null;
    private const int CLOSE_COMBO_HOLD_MS = 2000;

    // Alt+Tab combo detection
    private DateTime? _altTabComboStartTime = null;
    private bool _altTabFired = false;
    private const int ALT_TAB_QUICK_TAP_MS = 500;

    // Touchpad mouse control
    private bool _lastTouch1Active;
    private int _lastTouch1X, _lastTouch1Y;
    private DateTime _touchStartTime;
    private int _touchStartX, _touchStartY;
    private const float TOUCHPAD_SENSITIVITY = 0.8f;
    private const int TAP_MAX_DURATION_MS = 200;
    private const int TAP_MAX_MOVEMENT = 30;

    // Right stick mouse control (same as DirectInput)
    private const int STICK_CENTER = 128;
    private const int STICK_DEADZONE = 20;
    private const float MOUSE_SPEED_MAX = 20.0f;
    private const float MOUSE_SPEED_MIN = 2.0f;
    private DateTime _lastRightStickActivity = DateTime.MinValue;
    private const int MOUSE_MODE_TIMEOUT_MS = 1000;

    public bool IsConnected => _device != null && _stream != null;
    public GamepadType ControllerType => GamepadType.PlayStation;

    /// <summary>
    /// Try to connect to a DualShock 4 or DualSense controller
    /// </summary>
    public bool TryConnect()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices();

            foreach (var device in devices)
            {
                if (device.VendorID != SONY_VID) continue;

                if (IsPlayStationController(device.ProductID))
                {
                    try
                    {
                        // Try to open the device
                        if (device.TryOpen(out var stream))
                        {
                            _device = device;
                            _stream = stream;
                            _isBluetooth = device.GetMaxInputReportLength() > 64;

                            var productName = GetProductName(device.ProductID);
                            var connType = _isBluetooth ? "Bluetooth" : "USB";
                            Debug.WriteLine($"[DS4HidService] Connected to {productName} via {connType}");
                            Debug.WriteLine($"[DS4HidService] VID:PID = {device.VendorID:X4}:{device.ProductID:X4}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DS4HidService] Failed to open device: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DS4HidService] Error enumerating devices: {ex.Message}");
        }

        return false;
    }

    private bool IsPlayStationController(int productId)
    {
        return productId == DS4_V1_PID ||
               productId == DS4_V2_PID ||
               productId == DUALSENSE_PID ||
               productId == DUALSENSE_EDGE_PID;
    }

    private string GetProductName(int productId)
    {
        return productId switch
        {
            DS4_V1_PID => "DualShock 4 v1",
            DS4_V2_PID => "DualShock 4 v2",
            DUALSENSE_PID => "DualSense",
            DUALSENSE_EDGE_PID => "DualSense Edge",
            _ => "Unknown PlayStation Controller"
        };
    }

    public void StartPolling()
    {
        if (_stream == null)
        {
            Debug.WriteLine("[DS4HidService] Cannot start polling - not connected");
            return;
        }

        _cts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollLoop(_cts.Token));
        Debug.WriteLine("[DS4HidService] Started polling");
    }

    public void StopPolling()
    {
        _cts?.Cancel();
        try
        {
            _pollingTask?.Wait(1000);
        }
        catch { }

        _stream?.Close();
        _stream = null;
        _device = null;

        Debug.WriteLine("[DS4HidService] Stopped polling");
    }

    private void PollLoop(CancellationToken ct)
    {
        var buffer = new byte[128]; // Large enough for both USB and BT reports

        while (!ct.IsCancellationRequested && _stream != null)
        {
            try
            {
                int bytesRead = _stream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    ParseInputReport(buffer, bytesRead);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.WriteLine($"[DS4HidService] Read error: {ex.Message}");
                    _stream = null;
                    _device = null;
                }
                break;
            }
        }
    }

    private void ParseInputReport(byte[] report, int length)
    {
        // Determine offset based on connection type
        // USB: Report starts at byte 0 (Report ID 0x01)
        // Bluetooth: Report has 2-byte header, data starts at byte 2
        int offset = 0;

        // Check report ID for USB
        if (report[0] == 0x01)
        {
            offset = 1; // USB report, skip report ID
        }
        else if (report[0] == 0x11)
        {
            offset = 3; // Bluetooth report, skip header
        }
        else
        {
            return; // Unknown report type
        }

        // Parse analog sticks
        byte leftStickX = report[offset + 0];
        byte leftStickY = report[offset + 1];
        byte rightStickX = report[offset + 2];
        byte rightStickY = report[offset + 3];

        // Parse buttons
        byte buttons1 = report[offset + 4]; // D-pad + Square/Cross/Circle/Triangle
        byte buttons2 = report[offset + 5]; // L1/R1/L2/R2/Share/Options/L3/R3
        byte buttons3 = report[offset + 6]; // PS button, Touchpad click

        // Process inputs
        ProcessButtons(buttons1, buttons2, buttons3);
        ProcessDpad(buttons1);
        ProcessLeftStick(leftStickX, leftStickY);
        ProcessRightStickMouse(rightStickX, rightStickY);
        ProcessTouchpad(report, offset);
        CheckCloseCombo(buttons2);
        CheckAltTabCombo(buttons2);
    }

    private void ProcessButtons(byte buttons1, byte buttons2, byte buttons3)
    {
        // Check for new button presses
        byte newButtons1 = (byte)(buttons1 & ~_lastButtons1);
        byte newButtons2 = (byte)(buttons2 & ~_lastButtons2);
        byte newButtons3 = (byte)(buttons3 & ~_lastButtons3);

        // Fire raw button event for mapping mode
        if (newButtons1 != 0 || newButtons2 != 0 || newButtons3 != 0)
        {
            uint rawButton = (uint)((newButtons3 << 16) | (newButtons2 << 8) | newButtons1);
            if (rawButton != 0)
            {
                OnRawButtonPressed?.Invoke(rawButton);
            }
        }

        // Cross = A (confirm)
        if ((newButtons1 & BUTTON_CROSS) != 0)
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

        // Circle = B (back)
        if ((newButtons1 & BUTTON_CIRCLE) != 0)
            OnButtonPressed?.Invoke(GamepadButton.B);

        // Square = X
        if ((newButtons1 & BUTTON_SQUARE) != 0)
            OnButtonPressed?.Invoke(GamepadButton.X);

        // Triangle = Y
        if ((newButtons1 & BUTTON_TRIANGLE) != 0)
            OnButtonPressed?.Invoke(GamepadButton.Y);

        // L1 = Left Bumper
        if ((newButtons2 & BUTTON_L1) != 0)
            OnButtonPressed?.Invoke(GamepadButton.LeftBumper);

        // R1 = Right Bumper
        if ((newButtons2 & BUTTON_R1) != 0)
            OnButtonPressed?.Invoke(GamepadButton.RightBumper);

        // Options = Start
        if ((newButtons2 & BUTTON_OPTIONS) != 0)
            OnButtonPressed?.Invoke(GamepadButton.Start);

        // Share = Back
        if ((newButtons2 & BUTTON_SHARE) != 0)
            OnButtonPressed?.Invoke(GamepadButton.Back);

        _lastButtons1 = buttons1;
        _lastButtons2 = buttons2;
        _lastButtons3 = buttons3;
    }

    private void ProcessDpad(byte buttons1)
    {
        GamepadDirection direction = GamepadDirection.None;
        byte dpad = (byte)(buttons1 & DPAD_MASK);

        // DS4 D-pad values: 0=Up, 1=UpRight, 2=Right, 3=DownRight, 4=Down, 5=DownLeft, 6=Left, 7=UpLeft, 8=Released
        direction = dpad switch
        {
            0 => GamepadDirection.Up,
            1 => GamepadDirection.Up,      // Up-Right -> Up
            2 => GamepadDirection.Right,
            3 => GamepadDirection.Down,    // Down-Right -> Down
            4 => GamepadDirection.Down,
            5 => GamepadDirection.Down,    // Down-Left -> Down
            6 => GamepadDirection.Left,
            7 => GamepadDirection.Up,      // Up-Left -> Up
            _ => GamepadDirection.None
        };

        HandleDirection(direction);
    }

    private void ProcessLeftStick(byte x, byte y)
    {
        GamepadDirection direction = GamepadDirection.None;

        int xOffset = x - STICK_CENTER;
        int yOffset = y - STICK_CENTER;

        if (Math.Abs(yOffset) > STICK_DEADZONE || Math.Abs(xOffset) > STICK_DEADZONE)
        {
            if (Math.Abs(yOffset) > Math.Abs(xOffset))
            {
                direction = yOffset < 0 ? GamepadDirection.Up : GamepadDirection.Down;
            }
            else
            {
                direction = xOffset < 0 ? GamepadDirection.Left : GamepadDirection.Right;
            }
        }

        // Only handle if D-pad isn't active
        if (_lastDirection == GamepadDirection.None || direction != GamepadDirection.None)
        {
            HandleDirection(direction);
        }
    }

    private void HandleDirection(GamepadDirection direction)
    {
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

    private void ProcessRightStickMouse(byte x, byte y)
    {
        int xOffset = x - STICK_CENTER;
        int yOffset = y - STICK_CENTER;

        if (Math.Abs(xOffset) < STICK_DEADZONE) xOffset = 0;
        if (Math.Abs(yOffset) < STICK_DEADZONE) yOffset = 0;

        if (xOffset != 0 || yOffset != 0)
        {
            _lastRightStickActivity = DateTime.Now;

            float magnitude = (float)Math.Sqrt(xOffset * xOffset + yOffset * yOffset);
            float maxMagnitude = 127 - STICK_DEADZONE;
            float speedFactor = Math.Min(magnitude / maxMagnitude, 1.0f);
            float speed = MOUSE_SPEED_MIN + (MOUSE_SPEED_MAX - MOUSE_SPEED_MIN) * speedFactor;

            float normalizedX = xOffset / magnitude;
            float normalizedY = yOffset / magnitude;
            int moveX = (int)(normalizedX * speed * speedFactor);
            int moveY = (int)(normalizedY * speed * speedFactor);

            if (moveX != 0 || moveY != 0)
            {
                if (GetCursorPos(out POINT currentPos))
                {
                    SetCursorPos(currentPos.X + moveX, currentPos.Y + moveY);
                }
            }
        }
    }

    private void ProcessTouchpad(byte[] report, int offset)
    {
        // Touchpad data starts at offset 33 for USB (offset + 33)
        // For DS4: 2 touch points, each with ID + coordinates
        int touchOffset = offset + 33;

        if (touchOffset + 8 > report.Length) return;

        // First touch point
        byte touch1Packet = report[touchOffset];
        bool touch1Active = (touch1Packet & 0x80) == 0; // Bit 7 = 0 means active

        if (touch1Active)
        {
            // Parse 12-bit X and Y coordinates
            int touch1X = report[touchOffset + 1] | ((report[touchOffset + 2] & 0x0F) << 8);
            int touch1Y = ((report[touchOffset + 2] & 0xF0) >> 4) | (report[touchOffset + 3] << 4);

            if (_lastTouch1Active)
            {
                // Calculate delta movement
                int deltaX = touch1X - _lastTouch1X;
                int deltaY = touch1Y - _lastTouch1Y;

                // Move mouse cursor
                int moveX = (int)(deltaX * TOUCHPAD_SENSITIVITY);
                int moveY = (int)(deltaY * TOUCHPAD_SENSITIVITY);

                if (moveX != 0 || moveY != 0)
                {
                    if (GetCursorPos(out POINT currentPos))
                    {
                        SetCursorPos(currentPos.X + moveX, currentPos.Y + moveY);
                    }
                    _lastRightStickActivity = DateTime.Now; // Keep mouse mode active
                }
            }
            else
            {
                // Touch started
                _touchStartTime = DateTime.Now;
                _touchStartX = touch1X;
                _touchStartY = touch1Y;
            }

            _lastTouch1X = touch1X;
            _lastTouch1Y = touch1Y;
        }
        else if (_lastTouch1Active)
        {
            // Touch ended - check for tap
            var touchDuration = (DateTime.Now - _touchStartTime).TotalMilliseconds;
            int totalMovement = Math.Abs(_lastTouch1X - _touchStartX) + Math.Abs(_lastTouch1Y - _touchStartY);

            if (touchDuration < TAP_MAX_DURATION_MS && totalMovement < TAP_MAX_MOVEMENT)
            {
                // It's a tap - simulate click
                SimulateMouseClick();
                Debug.WriteLine("[DS4HidService] Touchpad tap -> click");
            }
        }

        _lastTouch1Active = touch1Active;
    }

    private void CheckCloseCombo(byte buttons2)
    {
        // L1 + R1 + Options (Start)
        bool isComboPressed =
            (buttons2 & BUTTON_L1) != 0 &&
            (buttons2 & BUTTON_R1) != 0 &&
            (buttons2 & BUTTON_OPTIONS) != 0;

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

    private void CheckAltTabCombo(byte buttons2)
    {
        bool l1Pressed = (buttons2 & BUTTON_L1) != 0;
        bool r1Pressed = (buttons2 & BUTTON_R1) != 0;
        bool optionsPressed = (buttons2 & BUTTON_OPTIONS) != 0;
        bool isAltTabCombo = l1Pressed && r1Pressed && !optionsPressed;

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
        Debug.WriteLine("[DS4HidService] Alt+Tab simulated via L1+R1");
    }

    public void ResetNavigationState()
    {
        _lastDirection = GamepadDirection.None;
        _lastNavigationTime = DateTime.MinValue;
        _isHoldingDirection = false;
    }

    public void UpdateButtonMapping(ControllerConfig config)
    {
        // DS4 uses fixed button mapping based on HID report structure
        // This method is here for interface compatibility
        Debug.WriteLine("[DS4HidService] Button mapping update (using fixed PS layout)");
    }

    public void Dispose()
    {
        StopPolling();
        _cts?.Dispose();
    }
}

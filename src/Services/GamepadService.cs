using System.Runtime.InteropServices;

namespace Kotak.Services;

public enum GamepadButton
{
    A, B, X, Y,
    Start, Back,
    LeftBumper, RightBumper,
    DPadUp, DPadDown, DPadLeft, DPadRight
}

public enum GamepadDirection
{
    None, Up, Down, Left, Right
}

public class GamepadService : IDisposable
{
    // XInput P/Invoke
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState14(int dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState13(int dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState910(int dwUserIndex, ref XINPUT_STATE pState);

    private delegate int XInputGetStateDelegate(int dwUserIndex, ref XINPUT_STATE pState);
    private static XInputGetStateDelegate? _xInputGetState;

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

    // Button flags
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

    // Events
    public event Action<GamepadButton>? OnButtonPressed;
    public event Action<GamepadDirection>? OnDirectionChanged;
    public event Action? OnCloseComboHeld; // LB+RB+Start held for 3 seconds

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private uint _lastPacketNumber;
    private ushort _lastButtons;
    private GamepadDirection _lastDirection = GamepadDirection.None;

    // Repeat navigation settings
    private DateTime _lastNavigationTime = DateTime.MinValue;
    private readonly TimeSpan _navigationRepeatDelay = TimeSpan.FromMilliseconds(150);
    private readonly TimeSpan _initialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private bool _isHoldingDirection;

    // Close combo detection (LB+RB+Start held for 3 seconds)
    private DateTime? _closeComboStartTime = null;
    private const int CLOSE_COMBO_HOLD_MS = 3000;

    static GamepadService()
    {
        // Try to load XInput DLL in order of preference
        try
        {
            var testState = new XINPUT_STATE();
            if (XInputGetState14(0, ref testState) != -1 || true)
            {
                _xInputGetState = XInputGetState14;
                return;
            }
        }
        catch { }

        try
        {
            var testState = new XINPUT_STATE();
            if (XInputGetState13(0, ref testState) != -1 || true)
            {
                _xInputGetState = XInputGetState13;
                return;
            }
        }
        catch { }

        try
        {
            var testState = new XINPUT_STATE();
            if (XInputGetState910(0, ref testState) != -1 || true)
            {
                _xInputGetState = XInputGetState910;
                return;
            }
        }
        catch { }
    }

    public void StartPolling()
    {
        if (_xInputGetState == null)
        {
            System.Diagnostics.Debug.WriteLine("XInput not available - gamepad support disabled");
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var state = new XINPUT_STATE();
                int result = _xInputGetState!(0, ref state);

                if (result == 0) // SUCCESS
                {
                    ProcessGamepadState(state);
                }

                Thread.Sleep(16); // ~60Hz polling
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gamepad poll error: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessGamepadState(XINPUT_STATE state)
    {
        // Process button presses (only on press, not release)
        ProcessButtons(state.Gamepad.wButtons);

        // Process direction (D-pad + left stick) with repeat
        ProcessDirection(state.Gamepad);

        // Check for close combo (LB+RB+Start held for 3 seconds)
        CheckCloseCombo(state.Gamepad.wButtons);

        _lastPacketNumber = state.dwPacketNumber;
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
                _closeComboStartTime = null; // Reset to prevent repeated firing
            }
        }
        else
        {
            _closeComboStartTime = null;
        }
    }

    private void ProcessButtons(ushort buttons)
    {
        // Detect new button presses (not held)
        ushort newPresses = (ushort)(buttons & ~_lastButtons);
        _lastButtons = buttons;

        if ((newPresses & XINPUT_GAMEPAD_A) != 0) OnButtonPressed?.Invoke(GamepadButton.A);
        if ((newPresses & XINPUT_GAMEPAD_B) != 0) OnButtonPressed?.Invoke(GamepadButton.B);
        if ((newPresses & XINPUT_GAMEPAD_X) != 0) OnButtonPressed?.Invoke(GamepadButton.X);
        if ((newPresses & XINPUT_GAMEPAD_Y) != 0) OnButtonPressed?.Invoke(GamepadButton.Y);
        if ((newPresses & XINPUT_GAMEPAD_START) != 0) OnButtonPressed?.Invoke(GamepadButton.Start);
        if ((newPresses & XINPUT_GAMEPAD_BACK) != 0) OnButtonPressed?.Invoke(GamepadButton.Back);
        if ((newPresses & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) OnButtonPressed?.Invoke(GamepadButton.LeftBumper);
        if ((newPresses & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) OnButtonPressed?.Invoke(GamepadButton.RightBumper);
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

    public void Dispose()
    {
        StopPolling();
        _cts?.Dispose();
    }
}

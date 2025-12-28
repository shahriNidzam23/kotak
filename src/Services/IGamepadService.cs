using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// Gamepad button enum shared by all gamepad services
/// </summary>
public enum GamepadButton
{
    A, B, X, Y,
    Start, Back,
    LeftBumper, RightBumper,
    DPadUp, DPadDown, DPadLeft, DPadRight
}

/// <summary>
/// Gamepad direction enum for navigation
/// </summary>
public enum GamepadDirection
{
    None, Up, Down, Left, Right
}

/// <summary>
/// Controller type for identification
/// </summary>
public enum GamepadType
{
    None,
    XInput,      // Xbox 360, Xbox One, Xbox Series controllers
    DirectInput, // Generic controllers (Fantech, etc.)
    PlayStation  // DualShock 4, DualSense via DirectInput
}

/// <summary>
/// Interface for gamepad services (XInput, DirectInput)
/// </summary>
public interface IGamepadService : IDisposable
{
    /// <summary>
    /// Fired when a button is pressed
    /// </summary>
    event Action<GamepadButton>? OnButtonPressed;

    /// <summary>
    /// Fired when direction changes (D-pad or left stick)
    /// </summary>
    event Action<GamepadDirection>? OnDirectionChanged;

    /// <summary>
    /// Fired when LB+RB+Start is held for 2+ seconds
    /// </summary>
    event Action? OnCloseComboHeld;

    /// <summary>
    /// Fired when LB+RB is quickly tapped (Alt+Tab)
    /// </summary>
    event Action? OnAltTabRequested;

    /// <summary>
    /// Fired when any raw button is pressed (for controller mapping mode)
    /// </summary>
    event Action<uint>? OnRawButtonPressed;

    /// <summary>
    /// Whether a controller is connected
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The type of connected controller
    /// </summary>
    GamepadType ControllerType { get; }

    /// <summary>
    /// Start polling for gamepad input
    /// </summary>
    void StartPolling();

    /// <summary>
    /// Stop polling for gamepad input
    /// </summary>
    void StopPolling();

    /// <summary>
    /// Reset navigation state (clears direction repeat tracking)
    /// </summary>
    void ResetNavigationState();

    /// <summary>
    /// Update button mapping from config
    /// </summary>
    void UpdateButtonMapping(ControllerConfig config);
}

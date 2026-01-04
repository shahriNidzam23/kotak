using System.Diagnostics;
using Kotak.Models;

namespace Kotak.Services;

/// <summary>
/// Unified gamepad manager that coordinates XInput, DS4 HID, and DirectInput services.
/// Priority: DS4 HID (PlayStation) > XInput (Xbox) > DirectInput (Generic)
/// </summary>
public class GamepadManager : IDisposable
{
    private DS4HidService? _ds4HidService;
    private XInputGamepadService? _xinputService;
    private DirectInputGamepadService? _directInputService;
    private IGamepadService? _activeService;
    private System.Timers.Timer? _connectionCheckTimer;
    private bool _isPolling;
    private bool _disposed;
    private ControllerConfig? _currentConfig;

    /// <summary>
    /// Fired when a button is pressed
    /// </summary>
    public event Action<GamepadButton>? OnButtonPressed;

    /// <summary>
    /// Fired when direction changes (D-pad or left stick)
    /// </summary>
    public event Action<GamepadDirection>? OnDirectionChanged;

    /// <summary>
    /// Fired when LB+RB+Start is held for 2+ seconds
    /// </summary>
    public event Action? OnCloseComboHeld;

    /// <summary>
    /// Fired when LB+RB is quickly tapped (Alt+Tab)
    /// </summary>
    public event Action? OnAltTabRequested;

    /// <summary>
    /// Fired when any raw button is pressed (for controller mapping mode)
    /// </summary>
    public event Action<uint>? OnRawButtonPressed;

    /// <summary>
    /// Fired when controller connection status changes
    /// </summary>
    public event Action<bool, GamepadType>? OnConnectionChanged;

    /// <summary>
    /// Whether any controller is connected
    /// </summary>
    public bool IsConnected => _activeService?.IsConnected ?? false;

    /// <summary>
    /// The type of currently active controller
    /// </summary>
    public GamepadType ActiveControllerType => _activeService?.ControllerType ?? GamepadType.None;

    /// <summary>
    /// Start polling for gamepad input. Tries XInput first, falls back to DirectInput.
    /// </summary>
    public void StartPolling()
    {
        if (_isPolling) return;
        _isPolling = true;

        Debug.WriteLine("[GamepadManager] Starting gamepad polling...");

        // Try to connect to a controller
        TryConnectController();

        // Start periodic connection check (every 2 seconds)
        _connectionCheckTimer = new System.Timers.Timer(2000);
        _connectionCheckTimer.Elapsed += (s, e) => CheckControllerConnection();
        _connectionCheckTimer.AutoReset = true;
        _connectionCheckTimer.Start();
    }

    /// <summary>
    /// Stop polling for gamepad input
    /// </summary>
    public void StopPolling()
    {
        if (!_isPolling) return;
        _isPolling = false;

        Debug.WriteLine("[GamepadManager] Stopping gamepad polling...");

        _connectionCheckTimer?.Stop();
        _connectionCheckTimer?.Dispose();
        _connectionCheckTimer = null;

        StopActiveService();
    }

    /// <summary>
    /// Reset navigation state (clears direction repeat tracking)
    /// </summary>
    public void ResetNavigationState()
    {
        _activeService?.ResetNavigationState();
    }

    /// <summary>
    /// Update button mapping from config
    /// </summary>
    public void UpdateButtonMapping(ControllerConfig config)
    {
        _currentConfig = config;
        _activeService?.UpdateButtonMapping(config);
    }

    private void TryConnectController()
    {
        var previousType = ActiveControllerType;
        var wasConnected = IsConnected;

        // Try DS4 HID first (PlayStation controllers with touchpad support)
        if (_ds4HidService == null)
        {
            _ds4HidService = new DS4HidService();
        }

        if (_ds4HidService.TryConnect())
        {
            if (_activeService != _ds4HidService)
            {
                SwitchToService(_ds4HidService);
                Debug.WriteLine("[GamepadManager] Connected to PlayStation controller via HID (touchpad enabled)");
            }
            return;
        }

        // Try XInput (Xbox controllers)
        if (_xinputService == null)
        {
            _xinputService = new XInputGamepadService();
        }

        if (_xinputService.TryConnect())
        {
            if (_activeService != _xinputService)
            {
                SwitchToService(_xinputService);
                Debug.WriteLine("[GamepadManager] Connected to XInput controller (Xbox)");
            }
            return;
        }

        // Fall back to DirectInput (Generic controllers)
        if (_directInputService == null)
        {
            // Skip XInput and PlayStation devices since we already checked them
            _directInputService = new DirectInputGamepadService(skipXInputDevices: true);
        }

        if (_directInputService.TryConnect())
        {
            if (_activeService != _directInputService)
            {
                SwitchToService(_directInputService);
                var typeStr = _directInputService.ControllerType == GamepadType.PlayStation
                    ? "PlayStation (DirectInput fallback)" : "Generic DirectInput";
                Debug.WriteLine($"[GamepadManager] Connected to {typeStr} controller");
            }
            return;
        }

        // No controller found
        if (_activeService != null)
        {
            Debug.WriteLine("[GamepadManager] No controller connected");
            StopActiveService();

            if (wasConnected)
            {
                OnConnectionChanged?.Invoke(false, GamepadType.None);
            }
        }
    }

    private void SwitchToService(IGamepadService newService)
    {
        var previousType = ActiveControllerType;
        var wasConnected = IsConnected;

        // Unsubscribe from old service
        if (_activeService != null)
        {
            UnsubscribeFromService(_activeService);
            _activeService.StopPolling();
        }

        // Subscribe to new service
        _activeService = newService;
        SubscribeToService(newService);

        // Apply current config if available
        if (_currentConfig != null)
        {
            newService.UpdateButtonMapping(_currentConfig);
        }

        // Start polling on new service
        newService.StartPolling();

        // Notify connection change
        if (!wasConnected || previousType != newService.ControllerType)
        {
            OnConnectionChanged?.Invoke(true, newService.ControllerType);
        }
    }

    private void StopActiveService()
    {
        if (_activeService != null)
        {
            UnsubscribeFromService(_activeService);
            _activeService.StopPolling();
            _activeService = null;
        }
    }

    private void SubscribeToService(IGamepadService service)
    {
        service.OnButtonPressed += HandleButtonPressed;
        service.OnDirectionChanged += HandleDirectionChanged;
        service.OnCloseComboHeld += HandleCloseComboHeld;
        service.OnAltTabRequested += HandleAltTabRequested;
        service.OnRawButtonPressed += HandleRawButtonPressed;
    }

    private void UnsubscribeFromService(IGamepadService service)
    {
        service.OnButtonPressed -= HandleButtonPressed;
        service.OnDirectionChanged -= HandleDirectionChanged;
        service.OnCloseComboHeld -= HandleCloseComboHeld;
        service.OnAltTabRequested -= HandleAltTabRequested;
        service.OnRawButtonPressed -= HandleRawButtonPressed;
    }

    private void HandleButtonPressed(GamepadButton button) => OnButtonPressed?.Invoke(button);
    private void HandleDirectionChanged(GamepadDirection direction) => OnDirectionChanged?.Invoke(direction);
    private void HandleCloseComboHeld() => OnCloseComboHeld?.Invoke();
    private void HandleAltTabRequested() => OnAltTabRequested?.Invoke();
    private void HandleRawButtonPressed(uint button) => OnRawButtonPressed?.Invoke(button);

    private void CheckControllerConnection()
    {
        if (!_isPolling) return;

        // If we have an active service, check if it's still connected
        if (_activeService != null)
        {
            if (!_activeService.IsConnected)
            {
                Debug.WriteLine("[GamepadManager] Controller disconnected, searching for new controller...");
                StopActiveService();
                TryConnectController();
            }
            return;
        }

        // No active service, try to find a controller
        TryConnectController();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPolling();

        _ds4HidService?.Dispose();
        _xinputService?.Dispose();
        _directInputService?.Dispose();

        _ds4HidService = null;
        _xinputService = null;
        _directInputService = null;
        _activeService = null;
    }
}

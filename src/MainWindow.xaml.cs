using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Kotak.Bridge;
using Kotak.Services;

namespace Kotak;

public partial class MainWindow : Window
{
    // Win32 P/Invoke for window management
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    // Mouse simulation P/Invoke
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

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
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

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const int WHEEL_DELTA = 120;

    private JsBridge? _jsBridge;
    private GamepadManager? _gamepadManager;
    private AppConfigService? _configService;
    private WebRemoteService? _webRemoteService;

    // Window handle for Win32 operations
    private IntPtr _mainWindowHandle = IntPtr.Zero;

    // Web app state tracking
    private bool _isWebAppActive = false;
    private string? _activeWebAppUrl = null;
    private const string LAUNCHER_URL = "https://kotakui.local/index.html";

    // Controller mapping mode
    private bool _isControllerMappingMode = false;

    public MainWindow()
    {
        InitializeComponent();

        // Set window icon from logo
        SetWindowIcon();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Use PreviewKeyDown to capture keys before WebView2 handles them
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "WebUI", "assets", "logo.png");
            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                Icon = bitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set window icon: {ex.Message}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Get window handle for Win32 operations (BringToForeground)
        var helper = new WindowInteropHelper(this);
        _mainWindowHandle = helper.Handle;

        await InitializeWebView();
        StartGamepadPolling();
        StartWebRemote();
    }

    private async Task InitializeWebView()
    {
        try
        {
            // Create WebView2 environment with custom user data folder
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KOTAK", "WebView2Data");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            // Configure WebView2 settings
            var settings = webView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false; // Disable browser shortcuts
#if DEBUG
            settings.AreDevToolsEnabled = true;
#else
            settings.AreDevToolsEnabled = false;
#endif

            // Initialize services
            _configService = new AppConfigService();
            _jsBridge = new JsBridge(this, _configService);

            // Register JavaScript bridge
            webView.CoreWebView2.AddHostObjectToScript("bridge", _jsBridge);

            // Handle navigation completed to restore focus
            webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            // Load the UI
            await LoadWebUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task LoadWebUI()
    {
        var webUIPath = GetWebUIPath();
        var thumbnailsPath = GetThumbnailsPath();

        if (Directory.Exists(webUIPath))
        {
            // Set up virtual host mapping for WebUI files
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kotakui.local",
                webUIPath,
                CoreWebView2HostResourceAccessKind.Allow);

            // Also map thumbnails folder for app icons
            if (Directory.Exists(thumbnailsPath))
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "kotakthumbs.local",
                    thumbnailsPath,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            webView.CoreWebView2.Navigate("https://kotakui.local/index.html");
        }
        else
        {
            MessageBox.Show($"WebUI folder not found at: {webUIPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private string GetThumbnailsPath()
    {
        // Check parent folder (for development when running from bin)
        var parentPath = Path.Combine(AppContext.BaseDirectory, "..", "thumbnails");
        if (Directory.Exists(parentPath))
        {
            return Path.GetFullPath(parentPath);
        }
        // Check current folder (for production)
        return Path.Combine(AppContext.BaseDirectory, "thumbnails");
    }

    private string GetWebUIPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "WebUI");
    }

    private void StartGamepadPolling()
    {
        // Use GamepadManager for multi-controller support (Xbox, PlayStation, Generic)
        _gamepadManager = new GamepadManager();
        _gamepadManager.OnButtonPressed += GamepadService_OnButtonPressed;
        _gamepadManager.OnDirectionChanged += GamepadService_OnDirectionChanged;
        _gamepadManager.OnCloseComboHeld += GamepadService_OnCloseComboHeld;
        _gamepadManager.OnAltTabRequested += GamepadService_OnAltTabRequested;
        _gamepadManager.OnRawButtonPressed += GamepadService_OnRawButtonPressed;
        _gamepadManager.OnConnectionChanged += GamepadManager_OnConnectionChanged;

        // Apply initial controller config
        var controllerConfig = _configService?.GetControllerConfig();
        if (controllerConfig != null)
        {
            _gamepadManager.UpdateButtonMapping(controllerConfig);
        }

        _gamepadManager.StartPolling();

        // Subscribe to config changes to update button mapping
        if (_configService != null)
        {
            _configService.OnControllerConfigChanged += config =>
            {
                _gamepadManager?.UpdateButtonMapping(config);
            };
        }

        System.Diagnostics.Debug.WriteLine($"GamepadManager started. Connected: {_gamepadManager.IsConnected}, Type: {_gamepadManager.ActiveControllerType}");
    }

    private void GamepadManager_OnConnectionChanged(bool isConnected, GamepadType controllerType)
    {
        System.Diagnostics.Debug.WriteLine($"Controller connection changed: {isConnected}, Type: {controllerType}");

        // Notify UI about controller connection changes
        Dispatcher.Invoke(() =>
        {
            var message = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "controllerConnection",
                isConnected,
                controllerType = controllerType.ToString()
            });
            webView.CoreWebView2?.PostWebMessageAsString(message);
        });
    }

    private void GamepadService_OnAltTabRequested()
    {
        // Alt+Tab to switch windows (LB+RB quick tap)
        System.Diagnostics.Debug.WriteLine("Alt+Tab requested");
    }

    // Navigation methods for embedded web apps
    public void NavigateToWebApp(string url)
    {
        _activeWebAppUrl = url;
        _isWebAppActive = true;
        closeHintOverlay.Visibility = Visibility.Visible;
        webView.CoreWebView2?.Navigate(url);
    }

    public void NavigateToLauncher(bool skipSplash = false)
    {
        _isWebAppActive = false;
        _activeWebAppUrl = null;
        closeHintOverlay.Visibility = Visibility.Collapsed;
        var url = skipSplash ? LAUNCHER_URL + "?skipSplash=1" : LAUNCHER_URL;
        webView.CoreWebView2?.Navigate(url);
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Check if we navigated back to the launcher
        var currentUrl = webView.CoreWebView2?.Source ?? "";
        if (currentUrl.StartsWith("https://kotakui.local/"))
        {
            // Reset gamepad navigation state to allow immediate response
            _gamepadManager?.ResetNavigationState();

            // Restore focus to enable gamepad navigation
            Dispatcher.BeginInvoke(new Action(() =>
            {
                this.Activate();
                this.Focus();
                webView?.Focus();
                System.Diagnostics.Debug.WriteLine("Focus restored after navigation to launcher");
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void GamepadService_OnCloseComboHeld()
    {
        Dispatcher.Invoke(() =>
        {
            if (_isWebAppActive)
            {
                // Web app: navigate back to launcher UI (skip splash since user is returning)
                NavigateToLauncher(skipSplash: true);
            }
            else
            {
                // EXE app or launcher in background: bring to foreground
                BringToForeground();
            }
        });
    }

    /// <summary>
    /// Check if Kotak window is currently in foreground
    /// </summary>
    private bool IsWindowInForeground()
    {
        if (_mainWindowHandle == IntPtr.Zero) return false;
        return GetForegroundWindow() == _mainWindowHandle;
    }

    /// <summary>
    /// Brings Kotak window to the foreground (works even when behind other apps)
    /// Uses thread input attachment trick to bypass Windows foreground restrictions
    /// </summary>
    private void BringToForeground()
    {
        System.Diagnostics.Debug.WriteLine("BringToForeground called");

        try
        {
            // Restore if minimized
            if (IsIconic(_mainWindowHandle))
            {
                ShowWindow(_mainWindowHandle, SW_RESTORE);
            }

            // Get the foreground window's thread
            IntPtr foregroundWindow = GetForegroundWindow();
            uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
            uint currentThreadId = GetCurrentThreadId();

            // Attach to the foreground thread's input queue
            // This allows us to call SetForegroundWindow successfully
            bool attached = false;
            if (foregroundThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                System.Diagnostics.Debug.WriteLine($"AttachThreadInput: {attached}");
            }

            try
            {
                // Temporarily make window topmost to force it to front
                SetWindowPos(_mainWindowHandle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                // Bring to top
                BringWindowToTop(_mainWindowHandle);

                // Set as foreground window
                bool result = SetForegroundWindow(_mainWindowHandle);
                System.Diagnostics.Debug.WriteLine($"SetForegroundWindow result: {result}");

                // Remove topmost flag (we don't want to stay always on top)
                SetWindowPos(_mainWindowHandle, HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                // Show window
                ShowWindow(_mainWindowHandle, SW_SHOW);

                // WPF activation
                this.Activate();
                this.Focus();

                // Ensure WebView gets focus for navigation
                webView?.Focus();
            }
            finally
            {
                // Detach from the foreground thread
                if (attached)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BringToForeground error: {ex.Message}");
        }
    }

    private void GamepadService_OnButtonPressed(GamepadButton button)
    {
        Dispatcher.Invoke(() =>
        {
            // Only process button inputs when Kotak is in foreground
            // This prevents gamepad shortcuts from interfering with other apps
            if (!IsWindowInForeground()) return;

            // Handle web app gamepad controls
            if (_isWebAppActive)
            {
                HandleWebAppGamepadButton(button);
                return;
            }

            var message = JsonSerializer.Serialize(new
            {
                type = "gamepad",
                action = "button",
                button = button.ToString()
            });
            webView.CoreWebView2?.PostWebMessageAsString(message);
        });
    }

    private void GamepadService_OnDirectionChanged(GamepadDirection direction)
    {
        Dispatcher.Invoke(() =>
        {
            // Only process direction inputs when Kotak is in foreground
            // This prevents gamepad shortcuts from interfering with other apps
            if (!IsWindowInForeground()) return;

            // Handle web app scrolling with D-pad
            if (_isWebAppActive)
            {
                HandleWebAppGamepadDirection(direction);
                return;
            }

            var message = JsonSerializer.Serialize(new
            {
                type = "gamepad",
                action = "direction",
                direction = direction.ToString()
            });
            webView.CoreWebView2?.PostWebMessageAsString(message);
        });
    }

    // ============================
    // Web App Gamepad Controls
    // ============================

    private void HandleWebAppGamepadButton(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.A:
                // Simulate click on focused element or center of screen
                ExecuteWebAppScript(@"
                    (function() {
                        var el = document.activeElement;
                        if (el && el !== document.body) {
                            el.click();
                        } else {
                            // Try to click center of viewport
                            var x = window.innerWidth / 2;
                            var y = window.innerHeight / 2;
                            var target = document.elementFromPoint(x, y);
                            if (target) target.click();
                        }
                    })();
                ");
                break;

            case GamepadButton.B:
            case GamepadButton.Back:
                // Go back in browser history
                ExecuteWebAppScript("window.history.back();");
                break;

            case GamepadButton.X:
                // Close web app and return to launcher
                NavigateToLauncher();
                break;
        }
    }

    private void HandleWebAppGamepadDirection(GamepadDirection direction)
    {
        int scrollAmount = 150; // pixels to scroll
        string script = direction switch
        {
            GamepadDirection.Up => $"window.scrollBy(0, -{scrollAmount});",
            GamepadDirection.Down => $"window.scrollBy(0, {scrollAmount});",
            GamepadDirection.Left => $"window.scrollBy(-{scrollAmount}, 0);",
            GamepadDirection.Right => $"window.scrollBy({scrollAmount}, 0);",
            _ => ""
        };

        if (!string.IsNullOrEmpty(script))
        {
            ExecuteWebAppScript(script);
        }
    }

    private async void ExecuteWebAppScript(string script)
    {
        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExecuteWebAppScript error: {ex.Message}");
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Escape closes web app (keyboard equivalent of LB+RB+Start)
        if (_isWebAppActive && e.Key == Key.Escape &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            NavigateToLauncher();
            e.Handled = true;
            return;
        }

        // Handle keyboard navigation in web apps
        if (_isWebAppActive)
        {
            switch (e.Key)
            {
                case Key.Up:
                    HandleWebAppGamepadDirection(GamepadDirection.Up);
                    e.Handled = true;
                    return;
                case Key.Down:
                    HandleWebAppGamepadDirection(GamepadDirection.Down);
                    e.Handled = true;
                    return;
                case Key.Left:
                    HandleWebAppGamepadDirection(GamepadDirection.Left);
                    e.Handled = true;
                    return;
                case Key.Right:
                    HandleWebAppGamepadDirection(GamepadDirection.Right);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    HandleWebAppGamepadButton(GamepadButton.A);
                    e.Handled = true;
                    return;
                case Key.Escape:
                case Key.Back:
                case Key.B:
                    HandleWebAppGamepadButton(GamepadButton.B);
                    e.Handled = true;
                    return;
                case Key.X:
                    // Close web app and return to launcher
                    HandleWebAppGamepadButton(GamepadButton.X);
                    e.Handled = true;
                    return;
            }
        }

        // When in controller mapping mode, allow number keys 1-9 to simulate button presses
        if (_isControllerMappingMode)
        {
            uint? simulatedButton = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0x0001, // B1
                Key.D2 or Key.NumPad2 => 0x0002, // B2
                Key.D3 or Key.NumPad3 => 0x0004, // B3
                Key.D4 or Key.NumPad4 => 0x0008, // B4
                Key.D5 or Key.NumPad5 => 0x0010, // B5
                Key.D6 or Key.NumPad6 => 0x0020, // B6
                Key.D7 or Key.NumPad7 => 0x0040, // B7
                Key.D8 or Key.NumPad8 => 0x0080, // B8
                Key.D9 or Key.NumPad9 => 0x0100, // B9
                _ => null
            };

            if (simulatedButton.HasValue)
            {
                var message = JsonSerializer.Serialize(new
                {
                    type = "controllerMapping",
                    rawButton = simulatedButton.Value,
                    buttonName = $"B{GetButtonNumber(simulatedButton.Value)}"
                });
                webView.CoreWebView2?.PostWebMessageAsString(message);
                e.Handled = true;
                return;
            }
        }

        // Map keyboard to gamepad-like input
        string? direction = null;
        string? button = null;

        switch (e.Key)
        {
            case Key.Up:
                direction = "Up";
                break;
            case Key.Down:
                direction = "Down";
                break;
            case Key.Left:
                direction = "Left";
                break;
            case Key.Right:
                direction = "Right";
                break;
            case Key.Enter:
                button = "A";
                break;
            case Key.Escape:
                button = "Back";
                break;
            case Key.Back:
                button = "Back";
                break;
            case Key.B:
                button = "B";
                break;
            case Key.Y:
                button = "Y";
                break;
            case Key.X:
                button = "X";
                break;
            case Key.Space:
                button = "Start";
                break;
            case Key.Delete:
                button = "X";
                break;
        }

        if (direction != null)
        {
            var message = JsonSerializer.Serialize(new
            {
                type = "gamepad",
                action = "direction",
                direction
            });
            webView.CoreWebView2?.PostWebMessageAsString(message);
            e.Handled = true;
        }
        else if (button != null)
        {
            var message = JsonSerializer.Serialize(new
            {
                type = "gamepad",
                action = "button",
                button
            });
            webView.CoreWebView2?.PostWebMessageAsString(message);
            e.Handled = true;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _gamepadManager?.StopPolling();
        _gamepadManager?.Dispose();
        _webRemoteService?.Stop();
        _webRemoteService?.Dispose();
    }

    // ============================
    // Web Remote Control
    // ============================

    private void StartWebRemote()
    {
        _webRemoteService = new WebRemoteService();
        _webRemoteService.OnCommand += WebRemoteService_OnCommand;
        _webRemoteService.Start();

        System.Diagnostics.Debug.WriteLine($"[WebRemote] URL: {_webRemoteService.RemoteUrl}");
    }

    public string? GetWebRemoteUrl()
    {
        return _webRemoteService?.RemoteUrl;
    }

    /// <summary>
    /// Broadcast UI state to all connected Web Remote clients (called from JS)
    /// </summary>
    public void BroadcastUIState(string screen, string tab, int focusedIndex, string appsJson)
    {
        try
        {
            var apps = JsonSerializer.Deserialize<object[]>(appsJson) ?? Array.Empty<object>();
            var state = new
            {
                type = "state",
                screen,
                tab,
                focusedIndex,
                apps
            };
            _webRemoteService?.BroadcastState(state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebRemote] BroadcastUIState error: {ex.Message}");
        }
    }

    private void WebRemoteService_OnCommand(string action, string value)
    {
        // Handle commands from Web Remote on UI thread
        Dispatcher.Invoke(() =>
        {
            // Only process inputs when Kotak is in foreground (or always for remote)
            switch (action)
            {
                case "button":
                    if (Enum.TryParse<GamepadButton>(value, out var button))
                    {
                        if (_isWebAppActive)
                        {
                            HandleWebAppGamepadButton(button);
                        }
                        else
                        {
                            var message = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                type = "gamepad",
                                action = "button",
                                button = button.ToString()
                            });
                            webView.CoreWebView2?.PostWebMessageAsString(message);
                        }
                    }
                    break;

                case "direction":
                    if (Enum.TryParse<GamepadDirection>(value, out var direction))
                    {
                        if (_isWebAppActive)
                        {
                            HandleWebAppGamepadDirection(direction);
                        }
                        else
                        {
                            var message = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                type = "gamepad",
                                action = "direction",
                                direction = direction.ToString()
                            });
                            webView.CoreWebView2?.PostWebMessageAsString(message);
                        }
                    }
                    break;

                case "ping":
                    // Just a connection check, no action needed
                    break;

                case "text":
                    // Send text from remote to WebView to type into focused input
                    var textMsg = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "remoteText",
                        text = value
                    });
                    webView.CoreWebView2?.PostWebMessageAsString(textMsg);
                    break;

                case "mouseMove":
                    try
                    {
                        var moveData = JsonSerializer.Deserialize<JsonElement>(value);
                        int deltaX = moveData.GetProperty("x").GetInt32();
                        int deltaY = moveData.GetProperty("y").GetInt32();
                        MoveCursor(deltaX, deltaY);
                    }
                    catch { }
                    break;

                case "mouseClick":
                    if (value == "left")
                        SimulateMouseClick();
                    else if (value == "right")
                        SimulateRightClick();
                    break;

                case "scroll":
                    try
                    {
                        var scrollData = JsonSerializer.Deserialize<JsonElement>(value);
                        int scrollX = scrollData.GetProperty("x").GetInt32();
                        int scrollY = scrollData.GetProperty("y").GetInt32();
                        SimulateScroll(scrollX, scrollY);
                    }
                    catch { }
                    break;
            }
        });
    }

    // ============================
    // Controller Mapping Mode
    // ============================

    public bool IsGamepadConnected()
    {
        return _gamepadManager?.IsConnected ?? false;
    }

    public string GetGamepadType()
    {
        return _gamepadManager?.ActiveControllerType.ToString() ?? "None";
    }

    public void StartControllerMappingMode()
    {
        _isControllerMappingMode = true;
        System.Diagnostics.Debug.WriteLine("Controller mapping mode started");
    }

    public void StopControllerMappingMode()
    {
        _isControllerMappingMode = false;
        System.Diagnostics.Debug.WriteLine("Controller mapping mode stopped");
    }

    private void GamepadService_OnRawButtonPressed(uint rawButton)
    {
        // Only send raw button data when in mapping mode AND window is in foreground
        if (_isControllerMappingMode)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsWindowInForeground()) return;

                var message = JsonSerializer.Serialize(new
                {
                    type = "controllerMapping",
                    rawButton = rawButton,
                    buttonName = $"B{GetButtonNumber(rawButton)}"
                });
                webView.CoreWebView2?.PostWebMessageAsString(message);
            });
        }
    }

    private static int GetButtonNumber(uint rawValue)
    {
        // Convert bitmask to button number (1-16)
        for (int i = 0; i < 16; i++)
        {
            if (rawValue == (1u << i))
                return i + 1;
        }
        return 0;
    }

    // ============================
    // Mouse Simulation (Web Remote)
    // ============================

    private void MoveCursor(int deltaX, int deltaY)
    {
        if (GetCursorPos(out POINT pos))
        {
            SetCursorPos(pos.X + deltaX, pos.Y + deltaY);
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
    }

    private void SimulateRightClick()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_RIGHTUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private void SimulateScroll(int deltaX, int deltaY)
    {
        // Vertical scroll
        if (deltaY != 0)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_WHEEL;
            inputs[0].u.mi.mouseData = (uint)(deltaY * WHEEL_DELTA / 10);
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
        // Horizontal scroll
        if (deltaX != 0)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_HWHEEL;
            inputs[0].u.mi.mouseData = (uint)(deltaX * WHEEL_DELTA / 10);
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    // ============================
    // Simple Transfer Events
    // ============================

    public void SendTransferLog(string time, string message, bool isError)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "transferLog",
            time,
            message,
            isError
        });
        webView.CoreWebView2?.PostWebMessageAsString(msg);
    }

    public void SendTransferStatus(bool isRunning)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "transferStatus",
            isRunning
        });
        webView.CoreWebView2?.PostWebMessageAsString(msg);
    }

    // ============================
    // Update Progress Events
    // ============================

    public void SendUpdateProgress(string phase, int progress, string message)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "updateProgress",
            phase,
            progress,
            message
        });
        webView.CoreWebView2?.PostWebMessageAsString(msg);
    }

    public void SendUpdateError(string error)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "updateError",
            error
        });
        webView.CoreWebView2?.PostWebMessageAsString(msg);
    }
}

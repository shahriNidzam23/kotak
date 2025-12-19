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

    private JsBridge? _jsBridge;
    private RawInputGamepadService? _gamepadService; // Changed to DirectInput-compatible service
    private AppConfigService? _configService;

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
        // Use RawInputGamepadService for DirectInput compatibility (Fantech, generic gamepads)
        // Pass controller config for button mapping
        var controllerConfig = _configService?.GetControllerConfig();
        _gamepadService = new RawInputGamepadService(controllerConfig);
        _gamepadService.OnButtonPressed += GamepadService_OnButtonPressed;
        _gamepadService.OnDirectionChanged += GamepadService_OnDirectionChanged;
        _gamepadService.OnCloseComboHeld += GamepadService_OnCloseComboHeld;
        _gamepadService.OnRawButtonPressed += GamepadService_OnRawButtonPressed;
        _gamepadService.StartPolling();

        // Subscribe to config changes to update button mapping
        if (_configService != null)
        {
            _configService.OnControllerConfigChanged += config =>
            {
                _gamepadService?.UpdateButtonMapping(config);
            };
        }

        System.Diagnostics.Debug.WriteLine($"Gamepad service started. Connected: {_gamepadService.IsConnected}");
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
            _gamepadService?.ResetNavigationState();

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
        _gamepadService?.StopPolling();
        _gamepadService?.Dispose();
    }

    // ============================
    // Controller Mapping Mode
    // ============================

    public bool IsGamepadConnected()
    {
        return _gamepadService?.IsConnected ?? false;
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

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Kotak.Services;

/// <summary>
/// HTTP + WebSocket server for Web Remote control - allows controlling KOTAK from any device via browser
/// </summary>
public class WebRemoteService : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private int _port = 8889;
    private bool _isRunning;

    // WebSocket clients for real-time sync
    private readonly List<WebSocket> _clients = new();
    private readonly object _clientsLock = new();
    private string? _currentState;

    public event Action<string, string>? OnCommand; // action, value

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public string? RemoteUrl { get; private set; }

    /// <summary>
    /// Get the local IP address for other devices to connect
    /// </summary>
    public string? GetLocalIpAddress()
    {
        try
        {
            // Get all network interfaces
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var properties = ni.GetIPProperties();
                foreach (var ip in properties.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = ip.Address.ToString();
                        // Prefer 192.168.x.x addresses
                        if (ipStr.StartsWith("192.168."))
                            return ipStr;
                    }
                }
            }

            // Fallback: get any IPv4 address
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting local IP: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Start the Web Remote server
    /// </summary>
    public bool Start(int port = 8889)
    {
        if (_isRunning) return true;

        _port = port;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => ServerLoop(_cts.Token));

            _isRunning = true;

            var ip = GetLocalIpAddress();
            RemoteUrl = ip != null ? $"http://{ip}:{_port}/remote" : null;

            Debug.WriteLine($"[WebRemote] Started on {RemoteUrl}");
            return true;
        }
        catch (HttpListenerException ex)
        {
            Debug.WriteLine($"[WebRemote] HttpListener error: {ex.Message}");

            // Try alternate port
            if (port == 8889)
            {
                return Start(8890);
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemote] Server start error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop the Web Remote server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _cts?.Cancel();

            // Close all WebSocket connections
            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        if (client.State == WebSocketState.Open)
                        {
                            client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None).Wait(1000);
                        }
                    }
                    catch { }
                }
                _clients.Clear();
            }

            _listener?.Stop();
            _listener?.Close();
            _listener = null;
            _isRunning = false;
            RemoteUrl = null;
            Debug.WriteLine("[WebRemote] Stopped");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemote] Error stopping server: {ex.Message}");
        }
    }

    /// <summary>
    /// Broadcast UI state to all connected WebSocket clients
    /// </summary>
    public void BroadcastState(object state)
    {
        var json = JsonSerializer.Serialize(state);
        _currentState = json;

        lock (_clientsLock)
        {
            var deadClients = new List<WebSocket>();

            foreach (var client in _clients)
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                    {
                        var buffer = Encoding.UTF8.GetBytes(json);
                        _ = client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                    {
                        deadClients.Add(client);
                    }
                }
                catch
                {
                    deadClients.Add(client);
                }
            }

            foreach (var dead in deadClients)
            {
                _clients.Remove(dead);
            }
        }
    }

    private async Task ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebRemote] Server loop error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for local network access
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            var path = request.Url?.AbsolutePath ?? "/";

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
            }
            // WebSocket upgrade
            else if (request.IsWebSocketRequest && path == "/ws")
            {
                await HandleWebSocket(context, ct);
                return; // Don't close response for WebSocket
            }
            // Root redirect to /remote
            else if (request.HttpMethod == "GET" && path == "/")
            {
                response.StatusCode = 302;
                response.Headers.Add("Location", "/remote");
            }
            // Remote control page
            else if (request.HttpMethod == "GET" && path == "/remote")
            {
                await ServeRemotePage(response);
            }
            // HTTP command fallback
            else if (request.HttpMethod == "POST" && path == "/command")
            {
                await HandleCommand(request, response);
            }
            else
            {
                response.StatusCode = 404;
                await WriteResponse(response, "Not Found");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemote] Request handling error: {ex.Message}");
            response.StatusCode = 500;
            await WriteResponse(response, "Internal Server Error");
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private async Task HandleWebSocket(HttpListenerContext context, CancellationToken ct)
    {
        WebSocket? ws = null;

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            ws = wsContext.WebSocket;

            lock (_clientsLock)
            {
                _clients.Add(ws);
            }

            Debug.WriteLine($"[WebRemote] WebSocket connected. Total clients: {_clients.Count}");

            // Send current state immediately
            if (_currentState != null)
            {
                var buffer = Encoding.UTF8.GetBytes(_currentState);
                await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
            }

            // Listen for commands
            var receiveBuffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        ProcessWebSocketMessage(json);

                        // Send acknowledgment
                        var ack = Encoding.UTF8.GetBytes("{\"success\":true}");
                        await ws.SendAsync(new ArraySegment<byte>(ack), WebSocketMessageType.Text, true, ct);
                    }
                }
                catch (WebSocketException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemote] WebSocket error: {ex.Message}");
        }
        finally
        {
            if (ws != null)
            {
                lock (_clientsLock)
                {
                    _clients.Remove(ws);
                }
                Debug.WriteLine($"[WebRemote] WebSocket disconnected. Total clients: {_clients.Count}");

                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                    }
                    ws.Dispose();
                }
                catch { }
            }
        }
    }

    private void ProcessWebSocketMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "command";
            var action = root.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;
            var value = root.TryGetProperty("value", out var valueProp) ? valueProp.GetString() : null;

            if (type == "command" && action != null)
            {
                Debug.WriteLine($"[WebRemote] WS Command: {action} = {value}");
                OnCommand?.Invoke(action, value ?? "");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemote] Error parsing WS message: {ex.Message}");
        }
    }

    private async Task HandleCommand(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();

            var command = JsonSerializer.Deserialize<RemoteCommand>(body);
            if (command != null)
            {
                Debug.WriteLine($"[WebRemote] HTTP Command: {command.Action} = {command.Value}");
                OnCommand?.Invoke(command.Action ?? "", command.Value ?? "");

                response.StatusCode = 200;
                await WriteResponse(response, "{\"success\":true}");
            }
            else
            {
                response.StatusCode = 400;
                await WriteResponse(response, "{\"error\":\"Invalid command\"}");
            }
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteResponse(response, $"{{\"error\":\"{ex.Message}\"}}");
        }
    }

    private async Task ServeRemotePage(HttpListenerResponse response)
    {
        var html = GetRemotePageHtml();
        response.ContentType = "text/html; charset=utf-8";
        response.StatusCode = 200;
        await WriteResponse(response, html);
    }

    private string GetRemotePageHtml()
    {
        return @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
    <title>KOTAK Remote</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; touch-action: manipulation; -webkit-tap-highlight-color: transparent; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #0a0a15 0%, #1a1a2e 100%);
            color: #fff;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            user-select: none;
            -webkit-user-select: none;
        }

        .header {
            padding: 16px;
            text-align: center;
            border-bottom: 1px solid rgba(255,255,255,0.1);
        }
        h1 { color: #5E72E4; font-size: 22px; }

        .main { flex: 1; padding: 16px; overflow-y: auto; }
        .controller { max-width: 320px; margin: 0 auto; }

        /* Bumpers */
        .bumpers { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px; }
        .bumper-btn {
            padding: 16px;
            border: none;
            border-radius: 12px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            background: linear-gradient(145deg, #4a4a5a, #3a3a48);
            color: #fff;
            box-shadow: 0 4px 12px rgba(0,0,0,0.3);
            transition: all 0.1s ease;
        }
        .bumper-btn:active { transform: scale(0.95); background: #3a3a48; }

        /* Touchpad */
        .touchpad {
            width: 100%;
            height: 180px;
            background: linear-gradient(145deg, #1a1a28, #252538);
            border-radius: 20px;
            margin-bottom: 16px;
            display: flex;
            align-items: center;
            justify-content: center;
            touch-action: none;
            position: relative;
            box-shadow: inset 0 4px 12px rgba(0,0,0,0.4);
            border: 1px solid rgba(255,255,255,0.05);
        }
        .touchpad-hint {
            color: #444;
            font-size: 13px;
            pointer-events: none;
            text-align: center;
            line-height: 1.4;
        }
        .touchpad.active {
            background: linear-gradient(145deg, #252538, #2a2a40);
            border-color: rgba(94,114,228,0.3);
        }
        .touchpad.active .touchpad-hint { opacity: 0.3; }

        /* Action Buttons */
        .action-row { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin-bottom: 12px; }
        .action-btn {
            padding: 18px 12px;
            border: none;
            border-radius: 16px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.1s ease;
        }
        .action-btn:active { transform: scale(0.9); }
        .btn-b { background: linear-gradient(145deg, #E45E5E, #C74A4A); color: white; }
        .btn-x { background: linear-gradient(145deg, #5EA5E4, #4A8AC7); color: white; }
        .btn-y { background: linear-gradient(145deg, #E4C75E, #C7A94A); color: white; }

        /* Bottom Row */
        .bottom-row { display: grid; grid-template-columns: 1fr 2fr; gap: 12px; margin-bottom: 16px; }
        .btn-back {
            padding: 16px;
            border: none;
            border-radius: 16px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            background: linear-gradient(145deg, #3a3a4a, #2a2a38);
            color: #aaa;
        }
        .btn-start {
            padding: 16px;
            border: none;
            border-radius: 16px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            background: linear-gradient(145deg, #37FF94, #2BD47A);
            color: #0a0a15;
        }
        .btn-back:active, .btn-start:active { transform: scale(0.95); }

        /* Keyboard Section */
        .keyboard-section {
            margin-top: 16px;
            padding-top: 16px;
            border-top: 1px solid rgba(255,255,255,0.1);
        }
        .keyboard-toggle {
            width: 100%;
            padding: 14px;
            border: none;
            border-radius: 12px;
            font-size: 15px;
            font-weight: 600;
            cursor: pointer;
            background: linear-gradient(145deg, #2a2a3a, #1a1a28);
            color: #888;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
        }
        .keyboard-toggle:active { background: #1a1a28; }
        .keyboard-toggle.active { background: #5E72E4; color: #fff; }

        .keyboard-input-area {
            display: none;
            margin-top: 12px;
        }
        .keyboard-input-area.visible { display: block; }

        .text-input {
            width: 100%;
            padding: 14px;
            border: 2px solid rgba(255,255,255,0.1);
            border-radius: 12px;
            background: rgba(255,255,255,0.05);
            color: #fff;
            font-size: 16px;
            margin-bottom: 12px;
        }
        .text-input:focus {
            outline: none;
            border-color: #5E72E4;
        }
        .text-input::placeholder { color: #666; }

        .keyboard-actions {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 12px;
        }
        .kb-btn {
            padding: 14px;
            border: none;
            border-radius: 12px;
            font-size: 15px;
            font-weight: 600;
            cursor: pointer;
        }
        .kb-btn:active { transform: scale(0.95); }
        .kb-send { background: #5E72E4; color: #fff; }
        .kb-clear { background: rgba(255,255,255,0.1); color: #aaa; }

        /* Status */
        .status-bar {
            padding: 12px 16px;
            background: rgba(0,0,0,0.3);
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
            font-size: 13px;
        }
        .status-dot {
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: #666;
        }
        .status-dot.connected { background: #37FF94; }
        .status-dot.error { background: #E45E5E; }
    </style>
</head>
<body>
    <div class='header'>
        <h1>KOTAK Remote</h1>
    </div>

    <div class='main'>
        <div class='controller'>
            <!-- Bumpers -->
            <div class='bumpers'>
                <button class='bumper-btn' data-action='button' data-value='LB'>LB</button>
                <button class='bumper-btn' data-action='button' data-value='RB'>RB</button>
            </div>

            <!-- Touchpad -->
            <div class='touchpad' id='touchpad'>
                <div class='touchpad-hint'>Swipe: Move cursor<br>Tap: Click<br>2-finger: Right-click / Scroll</div>
            </div>

            <!-- Action Buttons -->
            <div class='action-row'>
                <button class='action-btn btn-b' data-action='button' data-value='B'>B</button>
                <button class='action-btn btn-x' data-action='button' data-value='X'>X</button>
                <button class='action-btn btn-y' data-action='button' data-value='Y'>Y</button>
            </div>

            <!-- Bottom Row -->
            <div class='bottom-row'>
                <button class='btn-back' data-action='button' data-value='Back'>Back</button>
                <button class='btn-start' data-action='button' data-value='Start'>Start</button>
            </div>

            <!-- Keyboard Section -->
            <div class='keyboard-section'>
                <button class='keyboard-toggle' id='keyboard-toggle' onclick='toggleKeyboard()'>
                    <span>&#9000;</span> Keyboard
                </button>
                <div class='keyboard-input-area' id='keyboard-area'>
                    <input type='text' class='text-input' id='text-input' placeholder='Type here...' autocomplete='off' autocapitalize='off'>
                    <div class='keyboard-actions'>
                        <button class='kb-btn kb-send' onclick='sendText()'>Send</button>
                        <button class='kb-btn kb-clear' onclick='clearText()'>Clear</button>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class='status-bar'>
        <div class='status-dot' id='status-dot'></div>
        <span id='status-text'>Connecting...</span>
    </div>

    <script>
        let ws = null;

        function connect() {
            const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
            ws = new WebSocket(protocol + '//' + location.host + '/ws');

            ws.onopen = () => updateStatus('connected', 'Connected');
            ws.onclose = () => { updateStatus('disconnected', 'Disconnected'); setTimeout(connect, 2000); };
            ws.onerror = () => updateStatus('error', 'Connection error');
        }

        function updateStatus(state, text) {
            document.getElementById('status-dot').className = 'status-dot ' + (state === 'connected' ? 'connected' : state === 'error' ? 'error' : '');
            document.getElementById('status-text').textContent = text;
        }

        function sendCommand(action, value) {
            const msg = JSON.stringify({ type: 'command', action, value });
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(msg);
            } else {
                fetch('/command', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action, value })
                }).catch(err => console.error('Command failed:', err));
            }
        }

        // Keyboard functions
        function toggleKeyboard() {
            const toggle = document.getElementById('keyboard-toggle');
            const area = document.getElementById('keyboard-area');
            const isVisible = area.classList.toggle('visible');
            toggle.classList.toggle('active', isVisible);
            if (isVisible) {
                document.getElementById('text-input').focus();
            }
        }

        function sendText() {
            const input = document.getElementById('text-input');
            const text = input.value;
            if (text) {
                sendCommand('text', text);
                input.value = '';
            }
        }

        function clearText() {
            document.getElementById('text-input').value = '';
            document.getElementById('text-input').focus();
        }

        // Handle Enter key in input
        document.getElementById('text-input').addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                sendText();
            }
        });

        // Button handlers
        document.querySelectorAll('[data-action]').forEach(btn => {
            const handler = (e) => {
                e.preventDefault();
                sendCommand(btn.dataset.action, btn.dataset.value);
            };
            btn.addEventListener('touchstart', handler);
            btn.addEventListener('mousedown', handler);
        });

        // Touchpad handling
        const touchpad = document.getElementById('touchpad');
        const touchState = {
            startX: 0, startY: 0,
            lastX: 0, lastY: 0,
            fingerCount: 0,
            startTime: 0,
            moved: false
        };
        const TAP_THRESHOLD = 15;
        const TAP_DURATION = 300;
        const CURSOR_SENSITIVITY = 1.5;
        const SCROLL_SENSITIVITY = 0.8;

        touchpad.addEventListener('touchstart', (e) => {
            e.preventDefault();
            touchpad.classList.add('active');
            const touches = e.touches;
            touchState.fingerCount = touches.length;
            touchState.startX = touchState.lastX = touches[0].clientX;
            touchState.startY = touchState.lastY = touches[0].clientY;
            touchState.startTime = Date.now();
            touchState.moved = false;
        });

        touchpad.addEventListener('touchmove', (e) => {
            e.preventDefault();
            const touches = e.touches;
            const deltaX = (touches[0].clientX - touchState.lastX) * CURSOR_SENSITIVITY;
            const deltaY = (touches[0].clientY - touchState.lastY) * CURSOR_SENSITIVITY;

            touchState.lastX = touches[0].clientX;
            touchState.lastY = touches[0].clientY;

            if (Math.abs(deltaX) > 1 || Math.abs(deltaY) > 1) {
                touchState.moved = true;

                if (touches.length === 1) {
                    sendCommand('mouseMove', JSON.stringify({
                        x: Math.round(deltaX),
                        y: Math.round(deltaY)
                    }));
                } else if (touches.length >= 2) {
                    sendCommand('scroll', JSON.stringify({
                        x: Math.round(-deltaX * SCROLL_SENSITIVITY),
                        y: Math.round(-deltaY * SCROLL_SENSITIVITY)
                    }));
                }
            }
        });

        touchpad.addEventListener('touchend', (e) => {
            e.preventDefault();
            touchpad.classList.remove('active');

            const duration = Date.now() - touchState.startTime;
            const totalMove = Math.abs(touchState.lastX - touchState.startX)
                            + Math.abs(touchState.lastY - touchState.startY);

            if (duration < TAP_DURATION && totalMove < TAP_THRESHOLD && !touchState.moved) {
                if (touchState.fingerCount === 1) {
                    sendCommand('mouseClick', 'left');
                } else if (touchState.fingerCount >= 2) {
                    sendCommand('mouseClick', 'right');
                }
            }

            touchState.fingerCount = e.touches.length;
        });

        connect();
    </script>
</body>
</html>";
    }

    private async Task WriteResponse(HttpListenerResponse response, string content)
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

public class RemoteCommand
{
    public string? Action { get; set; }
    public string? Value { get; set; }
}

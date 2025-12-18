using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Kotak.Services;

public class SimpleTransferService : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private string _savePath;
    private int _port = 8080;
    private bool _isRunning;

    public event Action<TransferLogEntry>? OnLogEntry;
    public event Action<bool>? OnStatusChanged;

    public bool IsRunning => _isRunning;
    public string SavePath => _savePath;
    public int Port => _port;

    public SimpleTransferService()
    {
        _savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public void SetSavePath(string path)
    {
        if (Directory.Exists(path))
        {
            _savePath = path;
        }
    }

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
    /// Start the HTTP server
    /// </summary>
    public bool Start(int port = 8080)
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
            OnStatusChanged?.Invoke(true);

            Log("Server started", false);
            return true;
        }
        catch (HttpListenerException ex)
        {
            // Port may require admin or is in use
            Log($"Failed to start: {ex.Message}", true);
            Debug.WriteLine($"HttpListener error: {ex.Message}");

            // Try alternate port
            if (port == 8080)
            {
                return Start(8888);
            }

            return false;
        }
        catch (Exception ex)
        {
            Log($"Failed to start: {ex.Message}", true);
            Debug.WriteLine($"Server start error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop the HTTP server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
            _isRunning = false;
            OnStatusChanged?.Invoke(false);
            Log("Server stopped", false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping server: {ex.Message}");
        }
    }

    private async Task ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context), ct);
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
                Debug.WriteLine($"Server loop error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.HttpMethod == "GET")
            {
                // Serve upload page
                await ServeUploadPage(response);
            }
            else if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/upload")
            {
                // Handle file upload
                await HandleFileUpload(request, response);
            }
            else
            {
                response.StatusCode = 404;
                await WriteResponse(response, "Not Found");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Request handling error: {ex.Message}");
            response.StatusCode = 500;
            await WriteResponse(response, "Internal Server Error");
        }
        finally
        {
            response.Close();
        }
    }

    private async Task ServeUploadPage(HttpListenerResponse response)
    {
        var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>KOTAK Transfer - Upload Files</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: #0a0a0f; color: #fff; min-height: 100vh;
            display: flex; align-items: center; justify-content: center;
            padding: 20px;
        }
        .container { max-width: 500px; width: 100%; text-align: center; }
        h1 { margin-bottom: 10px; color: #5E72E4; }
        p { color: #a0a0b0; margin-bottom: 30px; }
        .upload-area {
            border: 3px dashed #5E72E4; border-radius: 16px;
            padding: 60px 30px; margin-bottom: 20px;
            background: #151520; cursor: pointer;
            transition: all 0.3s ease;
        }
        .upload-area:hover, .upload-area.dragover {
            background: #1a1a28; border-color: #37FF94;
        }
        .upload-area .icon { font-size: 64px; margin-bottom: 20px; }
        .upload-area .text { font-size: 18px; color: #a0a0b0; }
        input[type=file] { display: none; }
        .progress { display: none; margin-top: 20px; }
        .progress-bar {
            height: 8px; background: #252538; border-radius: 4px; overflow: hidden;
        }
        .progress-fill { height: 100%; background: #5E72E4; width: 0; transition: width 0.3s; }
        .progress-text { margin-top: 10px; color: #a0a0b0; }
        .status { margin-top: 20px; padding: 15px; border-radius: 12px; display: none; }
        .status.success { background: rgba(55,255,148,0.2); color: #37FF94; }
        .status.error { background: rgba(244,67,54,0.2); color: #f44336; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>&#128225; KOTAK Transfer</h1>
        <p>Upload files to the connected PC</p>
        <form id='uploadForm' action='/upload' method='post' enctype='multipart/form-data'>
            <label class='upload-area' id='dropArea'>
                <div class='icon'>&#128194;</div>
                <div class='text'>Tap to select files or drag & drop</div>
                <input type='file' id='fileInput' name='files' multiple>
            </label>
            <div class='progress' id='progress'>
                <div class='progress-bar'><div class='progress-fill' id='progressFill'></div></div>
                <div class='progress-text' id='progressText'>Uploading...</div>
            </div>
            <div class='status' id='status'></div>
        </form>
    </div>
    <script>
        const dropArea = document.getElementById('dropArea');
        const fileInput = document.getElementById('fileInput');
        const progress = document.getElementById('progress');
        const progressFill = document.getElementById('progressFill');
        const progressText = document.getElementById('progressText');
        const status = document.getElementById('status');

        ['dragenter', 'dragover'].forEach(e => {
            dropArea.addEventListener(e, (ev) => { ev.preventDefault(); dropArea.classList.add('dragover'); });
        });
        ['dragleave', 'drop'].forEach(e => {
            dropArea.addEventListener(e, (ev) => { ev.preventDefault(); dropArea.classList.remove('dragover'); });
        });
        dropArea.addEventListener('drop', (e) => {
            fileInput.files = e.dataTransfer.files;
            uploadFiles(e.dataTransfer.files);
        });
        fileInput.addEventListener('change', () => uploadFiles(fileInput.files));

        async function uploadFiles(files) {
            if (!files.length) return;

            progress.style.display = 'block';
            status.style.display = 'none';

            const formData = new FormData();
            for (let f of files) formData.append('files', f);

            try {
                const xhr = new XMLHttpRequest();
                xhr.upload.onprogress = (e) => {
                    if (e.lengthComputable) {
                        const pct = (e.loaded / e.total) * 100;
                        progressFill.style.width = pct + '%';
                        progressText.textContent = 'Uploading... ' + Math.round(pct) + '%';
                    }
                };
                xhr.onload = () => {
                    progress.style.display = 'none';
                    status.style.display = 'block';
                    if (xhr.status === 200) {
                        status.className = 'status success';
                        status.textContent = '&#10004; ' + files.length + ' file(s) uploaded successfully!';
                    } else {
                        status.className = 'status error';
                        status.textContent = '&#10006; Upload failed: ' + xhr.responseText;
                    }
                    progressFill.style.width = '0';
                };
                xhr.onerror = () => {
                    progress.style.display = 'none';
                    status.style.display = 'block';
                    status.className = 'status error';
                    status.textContent = '&#10006; Network error';
                };
                xhr.open('POST', '/upload');
                xhr.send(formData);
            } catch (err) {
                progress.style.display = 'none';
                status.style.display = 'block';
                status.className = 'status error';
                status.textContent = '&#10006; Error: ' + err.message;
            }
        }
    </script>
</body>
</html>";

        response.ContentType = "text/html; charset=utf-8";
        response.StatusCode = 200;
        await WriteResponse(response, html);
    }

    private async Task HandleFileUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var boundary = GetBoundary(request.ContentType);
            if (string.IsNullOrEmpty(boundary))
            {
                response.StatusCode = 400;
                await WriteResponse(response, "Invalid request");
                return;
            }

            int fileCount = 0;
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var content = await reader.ReadToEndAsync();

            // Parse multipart form data
            var parts = content.Split(new[] { "--" + boundary }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (part.Trim() == "--" || string.IsNullOrWhiteSpace(part)) continue;

                // Extract filename
                var filenameMatch = Regex.Match(part, @"filename=""([^""]+)""");
                if (!filenameMatch.Success) continue;

                var filename = filenameMatch.Groups[1].Value;

                // Sanitize filename
                filename = Path.GetFileName(filename);
                var invalidChars = Path.GetInvalidFileNameChars();
                filename = string.Join("_", filename.Split(invalidChars));

                // Find the content (after double newline)
                var contentStart = part.IndexOf("\r\n\r\n");
                if (contentStart < 0) contentStart = part.IndexOf("\n\n");
                if (contentStart < 0) continue;

                contentStart += part.Contains("\r\n\r\n") ? 4 : 2;

                // Remove trailing boundary markers
                var fileContent = part.Substring(contentStart);
                if (fileContent.EndsWith("\r\n"))
                    fileContent = fileContent.Substring(0, fileContent.Length - 2);
                else if (fileContent.EndsWith("\n"))
                    fileContent = fileContent.Substring(0, fileContent.Length - 1);

                // Ensure save directory exists
                Directory.CreateDirectory(_savePath);

                // Handle duplicate filenames
                var savePath = Path.Combine(_savePath, filename);
                var counter = 1;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                var ext = Path.GetExtension(filename);
                while (File.Exists(savePath))
                {
                    savePath = Path.Combine(_savePath, $"{nameWithoutExt} ({counter}){ext}");
                    counter++;
                }

                // Write file
                await File.WriteAllTextAsync(savePath, fileContent, request.ContentEncoding);
                fileCount++;

                Log($"Received: {Path.GetFileName(savePath)}", false);
            }

            response.StatusCode = 200;
            await WriteResponse(response, $"Uploaded {fileCount} file(s)");
        }
        catch (Exception ex)
        {
            Log($"Upload error: {ex.Message}", true);
            response.StatusCode = 500;
            await WriteResponse(response, ex.Message);
        }
    }

    private string? GetBoundary(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        var match = Regex.Match(contentType, @"boundary=(.+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task WriteResponse(HttpListenerResponse response, string content)
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    private void Log(string message, bool isError)
    {
        OnLogEntry?.Invoke(new TransferLogEntry
        {
            Time = DateTime.Now,
            Message = message,
            IsError = isError
        });
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

public class TransferLogEntry
{
    public DateTime Time { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

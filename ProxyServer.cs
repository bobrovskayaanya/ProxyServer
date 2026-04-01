using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace proxy
{
    public class ProxyServer
    {
        private readonly int _listenPort;
        private TcpListener _listener;
        private bool _isRunning;

        public ProxyServer(int port)
        {
            _listenPort = port;
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _listenPort);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Proxy server started on port {_listenPort}");

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting client: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("Proxy server stopped");
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var clientStream = client.GetStream())
            {
                try
                {
                    var buffer = new byte[4096];
                    var bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                    var requestString = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    if (string.IsNullOrEmpty(requestString))
                    {
                        return;
                    }

                    var firstLine = requestString.Substring(0, requestString.IndexOf('\n')).Trim();
                    var parts = firstLine.Split(' ');
                    if (parts.Length < 3)
                    {
                        return;
                    }

                    var method = parts[0];
                    var fullUrl = parts[1];

                    Uri uri;
                    try
                    {
                        uri = new Uri(fullUrl);
                    }
                    catch (UriFormatException)
                    {
                        uri = new Uri("http://" + fullUrl);
                    }

                    var targetHost = uri.Host;
                    var targetPort = uri.Port;
                    var path = uri.PathAndQuery;

                    string requestUrl = fullUrl;
                    int statusCode = 0;

                    using (var targetClient = new TcpClient(targetHost, targetPort))
                    using (var targetStream = targetClient.GetStream())
                    {
                        var modifiedRequest = requestString.Replace(
                            $"{method} {fullUrl}",
                            $"{method} {path}");

                        var requestBytes = Encoding.ASCII.GetBytes(modifiedRequest);
                        await targetStream.WriteAsync(requestBytes, 0, requestBytes.Length);

                        var responseBuffer = new byte[4096];
                        var responseBytesRead = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length);

                        await clientStream.WriteAsync(responseBuffer, 0, responseBytesRead);

                        var responseString = Encoding.ASCII.GetString(responseBuffer, 0, responseBytesRead);
                        var responseFirstLine = responseString.Substring(0, responseString.IndexOf('\n')).Trim();
                        var responseParts = responseFirstLine.Split(' ');

                        if (responseParts.Length >= 2)
                        {
                            int.TryParse(responseParts[1], out statusCode);
                        }

                        Console.WriteLine($"{requestUrl} - {statusCode} {GetStatusText(statusCode)}");

                        while (responseBytesRead > 0)
                        {
                            responseBytesRead = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length);
                            if (responseBytesRead > 0)
                            {
                                await clientStream.WriteAsync(responseBuffer, 0, responseBytesRead);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
            }
        }

        private string GetStatusText(int statusCode)
        {
            return statusCode switch
            {
                200 => "OK",
                301 => "Moved Permanently",
                302 => "Found",
                304 => "Not Modified",
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                500 => "Internal Server Error",
                502 => "Bad Gateway",
                503 => "Service Unavailable",
                _ => "Unknown"
            };
        }
    }
}
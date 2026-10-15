using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CameraMaintainer.Tests;

// A real WebSocket transport, using the SignalR JSON handshake and record separator.
internal sealed class SignalRTestClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<JsonElement> _messages = Channel.CreateUnbounded<JsonElement>();
    private Task _reader = Task.CompletedTask;

    public static async Task<SignalRTestClient> ConnectAsync(Uri address, string session)
    {
        var client = new SignalRTestClient();
        try
        {
            client._socket.Options.SetRequestHeader("Cookie", "cm_session=" + session);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client._socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/hubs/events" }.Uri, timeout.Token);
            client._reader = client.ReadAsync();
            await client._socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
                WebSocketMessageType.Text, true, timeout.Token);
            var handshake = await client._messages.Reader.ReadAsync(timeout.Token);
            Assert.Equal("{}", handshake.GetRawText());
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task ExpectWorkspaceAsync(string topic)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await _messages.Reader.ReadAsync(timeout.Token);
            if (!message.TryGetProperty("target", out var target) || target.GetString() != "workspace.changed") continue;
            var payload = message.GetProperty("arguments")[0];
            Assert.Single(payload.EnumerateObject());
            Assert.Contains(topic, payload.GetProperty("topics").EnumerateArray().Select(item => item.GetString()));
            return;
        }
    }

    private async Task ReadAsync()
    {
        var buffer = new byte[4096];
        using var frame = new MemoryStream();
        var pending = "";
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                frame.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                pending += Encoding.UTF8.GetString(frame.ToArray());
                frame.SetLength(0);
                int separator;
                while ((separator = pending.IndexOf('\u001e')) >= 0)
                {
                    using var json = JsonDocument.Parse(pending[..separator]);
                    await _messages.Writer.WriteAsync(json.RootElement.Clone(), _stop.Token);
                    pending = pending[(separator + 1)..];
                }
            }
            _messages.Writer.TryComplete();
        }
        catch (Exception error) { _messages.Writer.TryComplete(error); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _socket.Abort();
        await _reader;
        _socket.Dispose();
        _stop.Dispose();
    }
}

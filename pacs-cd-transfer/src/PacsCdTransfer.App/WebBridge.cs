using System.Text.Json;
using System.Text.Json.Serialization;
using PacsCdTransfer.Core.Services;

namespace PacsCdTransfer.App;

/// <summary>
/// JS ↔ C# bridge for the WebView2-hosted mockup. JS calls <c>nativeCall(action, payload)</c>
/// (see app.html), which posts <c>{id, action, payload}</c> here; we dispatch to the real
/// backend and post <c>{id, result}</c> back so the pixel-identical UI is driven by real
/// DICOM/CD operations instead of the mockup's demo timers.
/// </summary>
public sealed class WebBridge
{
    private readonly AuthService _auth = new();

    private sealed record BridgeRequest(int Id, string Action, JsonElement Payload);
    private sealed record BridgeResponse(int Id, object? Result);

    public async Task<string> HandleAsync(string requestJson)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(requestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return "{}";
        }

        if (request is null) return "{}";

        object? result = request.Action switch
        {
            "login" => HandleLogin(request.Payload),
            _ => new { ok = false, error = "unknown action: " + request.Action }
        };

        var response = new BridgeResponse(request.Id, result);
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private object HandleLogin(JsonElement payload)
    {
        var username = payload.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
        var password = payload.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";

        var user = _auth.TryLogin(App.Settings, username, password);
        if (user is null) return new { ok = false };

        App.CurrentUser = user;
        return new { ok = true, username = user.Username, isAdmin = user.IsAdmin };
    }
}

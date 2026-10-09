using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SignIn;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

internal static class TestSettings
{
    public const string ApplicationId = "sandbox-test-app-id";
    public const string ApplicationSecret = "test-app-secret-value";

    /// <summary>Settings whose redirect address sits on a port that is free right now — never the real one.</summary>
    public static SquareConnectionSettings OnFreePort(string path = "/callback")
    {
        var raw = new SquareSettings
        {
            Environment = "sandbox",
            ApplicationId = ApplicationId,
            ApplicationSecret = ApplicationSecret,
            RedirectUri = $"http://localhost:{FreePort()}{path}",
        };
        Assert.True(SquareSettingsValidator.TryValidate(raw, out var settings, out var problems), string.Join("; ", problems));
        return settings;
    }

    public static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}

/// <summary>Stands in for Square: answers by "METHOD /path", records every request with its body.</summary>
internal sealed class StubSquare : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new();
    private readonly object _gate = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubSquare On(string method, string path, HttpStatusCode status, string json) =>
        On(method, path, (_, _) => Task.FromResult(Json(status, json)));

    public StubSquare On(string method, string path, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _routes[$"{method} {path}"] = responder;
        return this;
    }

    public static StubSquare ConnectedMerchant() => new StubSquare()
        .On("POST", "/oauth2/token", HttpStatusCode.OK,
            """{"access_token":"access-token-from-exchange","token_type":"bearer","expires_at":"2030-01-01T00:00:00Z","merchant_id":"MLR123","refresh_token":"refresh-token"}""")
        .On("GET", "/v2/merchants/me", HttpStatusCode.OK,
            """{"merchant":{"id":"MLR123","business_name":"Sunrise Coffee & Co","country":"US","currency":"USD","status":"ACTIVE","main_location_id":"L1"}}""")
        .On("GET", "/v2/locations", HttpStatusCode.OK,
            """
            {"locations":[
              {"id":"L1","name":"Main Street","status":"ACTIVE","merchant_id":"MLR123",
               "address":{"address_line_1":"1 Main St","locality":"Springfield","administrative_district_level_1":"IL","postal_code":"62701","country":"US"}},
              {"id":"L2","name":"Warehouse","status":"INACTIVE","merchant_id":"MLR123"}
            ]}
            """);

    public IEnumerable<RecordedRequest> To(string method, string path) =>
        Requests.Where(r => r.Method == method && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now: the SDK disposes request content before the call returns.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        lock (_gate)
        {
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!, path,
                request.Headers.Authorization?.ToString(), request.Content?.Headers.ContentType?.MediaType, body));
        }

        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var responder))
        {
            return Json(HttpStatusCode.NotFound, """{"errors":[{"category":"INVALID_REQUEST_ERROR","code":"NOT_FOUND","detail":"no stub"}]}""");
        }

        var response = await responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed record RecordedRequest(string Method, Uri Uri, string Path, string? Authorization, string? ContentType, string? Body);

/// <summary>
/// Plays the operator's browser: records each "open", then runs a script against the redirect
/// address the way Square's redirect would land there.
/// </summary>
internal sealed class ScriptedBrowser(Func<ScriptedBrowser, Uri, Task> script, bool opens = true) : IBrowserLauncher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public List<Uri> Opened { get; } = new();
    public List<BrowserVisit> Visits { get; } = new();
    public Task Script { get; private set; } = Task.CompletedTask;

    public bool TryOpen(Uri address)
    {
        Opened.Add(address);
        Script = Task.Run(() => script(this, address));
        return opens;
    }

    /// <summary>Visits the redirect URL named in <paramref name="signInPage"/> with the given query parameters.</summary>
    public async Task<BrowserVisit> LandOnRedirectAsync(Uri signInPage, params (string Name, string Value)[] parameters)
    {
        var redirect = Query(signInPage)["redirect_uri"]!;
        var query = string.Join("&", parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        using var response = await Http.GetAsync(query.Length == 0 ? redirect : $"{redirect}?{query}");
        var visit = new BrowserVisit(response.StatusCode, await response.Content.ReadAsStringAsync());
        lock (Visits)
        {
            Visits.Add(visit);
        }

        return visit;
    }

    public static string StateOf(Uri signInPage) => Query(signInPage)["state"]!;

    public static System.Collections.Specialized.NameValueCollection Query(Uri uri) => HttpUtility.ParseQueryString(uri.Query);
}

internal sealed record BrowserVisit(HttpStatusCode Status, string Html);

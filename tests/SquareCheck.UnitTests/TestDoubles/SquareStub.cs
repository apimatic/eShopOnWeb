using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests.TestDoubles;

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, string? Body)
{
    public string Route => $"{Method} {Uri.AbsolutePath}";
}

/// <summary>
/// Plays Square: answers by "METHOD /path", records every request (body buffered while still readable).
/// </summary>
public sealed class SquareStub : HttpMessageHandler
{
    public const string AccessToken = "EAAAtest-access-token";
    public const string MerchantId = "MLTEST123";
    public const string BusinessName = "Test <Shop> & Co";

    public const string TokenJson = $$"""
        {"access_token":"{{AccessToken}}","token_type":"bearer","expires_at":"2030-01-01T00:00:00Z",
         "merchant_id":"{{MerchantId}}","refresh_token":"EQAAtest-refresh-token"}
        """;

    public const string MerchantJson = """
        {"merchant":{"id":"MLTEST123","business_name":"Test <Shop> & Co","country":"US","language_code":"en-US",
         "currency":"USD","status":"ACTIVE","main_location_id":"LMAIN"}}
        """;

    public const string LocationsJson = """
        {"locations":[
          {"id":"LMAIN","name":"Main Street","status":"ACTIVE",
           "address":{"address_line_1":"1 Main St","locality":"Springfield","administrative_district_level_1":"IL",
                      "postal_code":"62701","country":"US"}},
          {"id":"LOLD","name":"Old Kiosk","status":"INACTIVE"}
        ]}
        """;

    private readonly Dictionary<string, Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>> _routes = new()
    {
        ["POST /oauth2/token"] = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, TokenJson)),
        ["GET /v2/merchants/me"] = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, MerchantJson)),
        ["GET /v2/locations"] = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, LocationsJson)),
    };

    private readonly object _gate = new();
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public IReadOnlyList<RecordedRequest> RequestsTo(string route) => Requests.Where(r => r.Route == route).ToList();

    public SquareStub On(string route, Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _routes[route] = responder;
        return this;
    }

    public SquareStub On(string route, HttpStatusCode status, string json) =>
        On(route, (_, _) => Task.FromResult(Json(status, json)));

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body);
        lock (_gate)
        {
            _requests.Add(recorded);
        }

        if (!_routes.TryGetValue(recorded.Route, out var responder))
        {
            return Json(HttpStatusCode.NotFound, """{"errors":[{"category":"INVALID_REQUEST_ERROR","code":"NOT_FOUND","detail":"no stub"}]}""");
        }

        var response = await responder(recorded, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}

using System.Net;

namespace Microsoft.eShopWeb.SubscriptionBillingTests;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that routes each request to the first matching rule. This is the
/// SDK's test seam: the client is constructed over an <see cref="HttpClient"/> backed by this handler, so no
/// real network calls happen. Request bodies are buffered during SendAsync (they are disposed per attempt).
/// </summary>
public sealed class RouteStubHandler : HttpMessageHandler
{
    public sealed record Rule(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond);

    private readonly List<Rule> _rules = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public int CountRequests(HttpMethod method, string absolutePathContains) =>
        Requests.Count(r => r.Method == method && (r.RequestUri?.AbsolutePath.Contains(absolutePathContains) ?? false));

    public RouteStubHandler On(Func<HttpRequestMessage, bool> match, HttpStatusCode status, string json)
    {
        _rules.Add(new Rule(match, _ => Json(status, json)));
        return this;
    }

    public RouteStubHandler On(HttpMethod method, Func<string, bool> pathMatch, HttpStatusCode status, string json) =>
        On(r => r.Method == method && pathMatch(r.RequestUri?.AbsolutePath ?? string.Empty), status, json);

    public RouteStubHandler On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _rules.Add(new Rule(match, respond));
        return this;
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => Json(status, json);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).Result);

        foreach (var rule in _rules)
        {
            if (rule.Match(request))
            {
                var response = rule.Respond(request);
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        var unmatched = Json(HttpStatusCode.NotImplemented,
            $"{{\"error\":\"no stub rule for {request.Method} {request.RequestUri?.AbsolutePath}\"}}");
        unmatched.RequestMessage = request;
        return Task.FromResult(unmatched);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}

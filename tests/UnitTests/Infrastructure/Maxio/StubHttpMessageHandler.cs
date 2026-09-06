using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Serves canned responses to the Maxio client and records what was sent, so the integration can be
/// exercised end to end without touching the network.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, StubResponse> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, StubResponse> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        int callIndex;
        lock (Requests)
        {
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.PathAndQuery, body));
            callIndex = Requests.Count - 1;
        }

        var stub = _responder(request, callIndex);

        return new HttpResponseMessage(stub.StatusCode)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, "application/json")
        };
    }

    public int CountOf(string method, string pathPrefix)
    {
        lock (Requests)
        {
            return Requests.Count(r => r.Method == method && r.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));
        }
    }
}

public record RecordedRequest(string Method, string PathAndQuery, string? Body);

public record StubResponse(HttpStatusCode StatusCode, string Body)
{
    public static StubResponse Ok(string body) => new(HttpStatusCode.OK, body);

    public static StubResponse Created(string body) => new(HttpStatusCode.Created, body);

    public static StubResponse NotFound() => new(HttpStatusCode.NotFound, "{\"errors\":[\"Customer not found\"]}");

    public static StubResponse Duplicate() => new(HttpStatusCode.Conflict, "{\"errors\":[\"DuplicatePrevention::DuplicateSubmissionError\"]}");

    public static StubResponse Unprocessable(string message) => new(HttpStatusCode.UnprocessableEntity, $"{{\"errors\":[\"{message}\"]}}");

    public static StubResponse Throttled() => new(HttpStatusCode.TooManyRequests, "{\"errors\":[\"Your request was denied due to a usage violation.\"]}");
}

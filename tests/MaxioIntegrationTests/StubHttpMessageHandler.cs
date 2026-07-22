using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The test seam for the Maxio SDK: the client is constructed from an HttpClient we supply, so a stub
/// handler lets us drive real SDK serialization, deserialization and error paths without a network call.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Every request the SDK actually issued, in order.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Responds to any request with a single canned JSON body.</summary>
    public static StubHttpMessageHandler Returning(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new StubHttpMessageHandler((_, _) => Json(json, status));
    }

    /// <summary>
    /// Responds to successive requests with successive bodies, so a multi-call flow can be driven without
    /// depending on the SDK's exact URL shapes. The final body is reused if more calls arrive.
    /// </summary>
    public static StubHttpMessageHandler Sequence(params string[] jsonBodies)
    {
        var index = 0;

        return new StubHttpMessageHandler((_, _) =>
        {
            var body = jsonBodies[Math.Min(index, jsonBodies.Length - 1)];
            index++;

            return Json(body);
        });
    }

    /// <summary>Fails every request with the given status and body.</summary>
    public static StubHttpMessageHandler Failing(HttpStatusCode status, string json = "{}")
    {
        return new StubHttpMessageHandler((_, _) => Json(json, status));
    }

    /// <summary>Succeeds with <paramref name="okJson"/> until the given call, which fails.</summary>
    public static StubHttpMessageHandler FailingAtCall(int failingCallNumber, HttpStatusCode status, string okJson)
    {
        var call = 0;

        return new StubHttpMessageHandler((_, _) =>
        {
            call++;

            return call == failingCallNumber ? Json("{}", status) : Json(okJson);
        });
    }

    /// <summary>Throws a transport failure, as an unreachable provider would.</summary>
    public static StubHttpMessageHandler Unreachable()
    {
        return new StubHttpMessageHandler((_, _) => throw new HttpRequestException("No such host is known."));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.ToString(), body));

        return _responder(request, body);
    }

    public sealed record RecordedRequest(string Method, string Url, string? Body);
}

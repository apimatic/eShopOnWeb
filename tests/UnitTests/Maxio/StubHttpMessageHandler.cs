using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// The SDK's testing seam: an <see cref="HttpMessageHandler"/> the client is constructed over, so no
/// real Maxio call is ever made. Every request is recorded — retries append, so the recording is what
/// proves how many times a write actually reached the wire.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Rule> _rules = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Responds to any request whose path contains <paramref name="pathFragment"/>.</summary>
    public StubHttpMessageHandler On(HttpMethod method, string pathFragment, HttpStatusCode status, string json)
    {
        _rules.Add(new Rule(method, pathFragment, _ => Respond(status, json)));
        return this;
    }

    /// <summary>Responds with a sequence, one entry per matching call; the last entry repeats.</summary>
    public StubHttpMessageHandler OnSequence(HttpMethod method, string pathFragment,
        params (HttpStatusCode Status, string Json)[] responses)
    {
        var calls = 0;
        _rules.Add(new Rule(method, pathFragment, _ =>
        {
            var index = Math.Min(calls++, responses.Length - 1);
            return Respond(responses[index].Status, responses[index].Json);
        }));
        return this;
    }

    /// <summary>Fails the matching request the way a dropped connection does.</summary>
    public StubHttpMessageHandler OnThrows(HttpMethod method, string pathFragment, Exception exception)
    {
        _rules.Add(new Rule(method, pathFragment, _ => throw exception));
        return this;
    }

    public int CountOf(HttpMethod method, string pathFragment) => Requests.Count(
        request => request.Method == method && request.Path.Contains(pathFragment, StringComparison.Ordinal));

    public RecordedRequest? LastOf(HttpMethod method, string pathFragment) => Requests.LastOrDefault(
        request => request.Method == method && request.Path.Contains(pathFragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.RequestUri!.AbsolutePath, body));

        var rule = _rules.FirstOrDefault(candidate => candidate.Matches(request));
        if (rule is null)
        {
            return Respond(HttpStatusCode.NotFound, "{}");
        }

        return rule.Respond(request);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Rule(HttpMethod Method, string PathFragment,
        Func<HttpRequestMessage, HttpResponseMessage> Respond)
    {
        public bool Matches(HttpRequestMessage request) =>
            request.Method == Method
            && request.RequestUri!.AbsolutePath.Contains(PathFragment, StringComparison.Ordinal);
    }

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Path, string Body);
}

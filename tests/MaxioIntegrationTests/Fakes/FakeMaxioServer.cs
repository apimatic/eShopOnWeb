using System.Net;
using System.Net.Http.Headers;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A stand-in for the Maxio Advanced Billing API: rules are matched on method plus a path
/// fragment, and every request is recorded so tests can assert what actually went on the wire.
/// </summary>
internal sealed class FakeMaxioServer : HttpMessageHandler
{
    private const string NotFoundBody = """{"errors":["Not Found"]}""";

    private readonly List<Rule> _rules = new();

    public List<RecordedRequest> Requests { get; } = new();

    public IReadOnlyList<RecordedRequest> RequestsFor(HttpMethod method, string pathContains) =>
        Requests.Where(request => request.Method == method && request.PathAndQuery.Contains(pathContains, StringComparison.OrdinalIgnoreCase)).ToList();

    public FakeMaxioServer Respond(HttpMethod method, string pathContains, HttpStatusCode status, string? json)
    {
        _rules.Add(new Rule(method, pathContains, new Queue<Reply>(new[] { new Reply(status, json, null) })));

        return this;
    }

    public FakeMaxioServer Respond(HttpMethod method, string pathContains, string json) =>
        Respond(method, pathContains, HttpStatusCode.OK, json);

    /// <summary>Replies in order, repeating the last reply once the sequence is exhausted.</summary>
    public FakeMaxioServer RespondInOrder(HttpMethod method, string pathContains, params (HttpStatusCode Status, string? Json)[] replies)
    {
        _rules.Add(new Rule(method, pathContains,
            new Queue<Reply>(replies.Select(reply => new Reply(reply.Status, reply.Json, null)))));

        return this;
    }

    public FakeMaxioServer Fail(HttpMethod method, string pathContains, Exception exception)
    {
        _rules.Add(new Rule(method, pathContains, new Queue<Reply>(new[] { new Reply(HttpStatusCode.OK, null, exception) })));

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        Requests.Add(new RecordedRequest(request.Method, pathAndQuery, body, request.Headers.Authorization));

        var rule = _rules.FirstOrDefault(candidate =>
            candidate.Method == request.Method &&
            pathAndQuery.Contains(candidate.PathContains, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            return Build(HttpStatusCode.NotFound, NotFoundBody);
        }

        var reply = rule.Replies.Count > 1 ? rule.Replies.Dequeue() : rule.Replies.Peek();
        if (reply.Exception is not null)
        {
            throw reply.Exception;
        }

        return Build(reply.Status, reply.Json);
    }

    private static HttpResponseMessage Build(HttpStatusCode status, string? json)
    {
        var response = new HttpResponseMessage(status);
        if (json is not null)
        {
            response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return response;
    }

    private sealed record Rule(HttpMethod Method, string PathContains, Queue<Reply> Replies);

    private sealed record Reply(HttpStatusCode Status, string? Json, Exception? Exception);
}

internal sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string Body, AuthenticationHeaderValue? Authorization);

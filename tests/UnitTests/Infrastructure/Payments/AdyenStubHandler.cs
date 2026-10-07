using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

/// <summary>
/// Stands in for Adyen at the HttpClient seam. Captures each request (and its body, read while it is still
/// readable) and answers with whatever the test's responder returns.
/// </summary>
public sealed class AdyenStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _responder;

    public AdyenStubHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

    public AdyenStubHandler(Func<int, HttpResponseMessage> responder) : this((_, n, _) => Task.FromResult(responder(n))) { }

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = await _responder(request, Requests.Count, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public string IdempotencyKeyOf(int index) => Requests[index].Headers.GetValues("Idempotency-Key").Single();
}

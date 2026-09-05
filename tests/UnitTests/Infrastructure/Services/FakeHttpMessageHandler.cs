using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

/// <summary>Minimal stub HttpMessageHandler for testing typed HttpClient services without a real network call.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string PathPrefix, HttpStatusCode StatusCode, string Body)> _stubs = new();
    private readonly List<(HttpMethod Method, string Path)> _requests = new();

    public FakeHttpMessageHandler When(HttpMethod method, string pathPrefix, HttpStatusCode statusCode, string body)
    {
        _stubs.Add((method, pathPrefix, statusCode, body));
        return this;
    }

    public int RequestCountFor(HttpMethod method, string pathPrefix) =>
        _requests.Count(r => r.Method == method && r.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath + request.RequestUri.Query);
        _requests.Add((request.Method, path));

        var stub = _stubs.FirstOrDefault(s => s.Method == request.Method && path.StartsWith(s.PathPrefix, StringComparison.Ordinal));
        if (stub.PathPrefix is null)
        {
            throw new InvalidOperationException($"No stub configured for {request.Method} {path}");
        }

        var response = new HttpResponseMessage(stub.StatusCode)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

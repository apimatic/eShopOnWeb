using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests;

public sealed class FixedStatusHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public FixedStatusHandler(HttpStatusCode statusCode, string body = "")
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode);
        if (_body.Length > 0)
        {
            response.Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}

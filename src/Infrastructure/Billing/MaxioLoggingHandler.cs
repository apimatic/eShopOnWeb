using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioLoggingHandler> _logger;

    public MaxioLoggingHandler(ILogger<MaxioLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation(
            "Maxio {Method} {Path} -> {StatusCode}",
            request.Method,
            request.RequestUri?.GetLeftPart(System.UriPartial.Path),
            (int)response.StatusCode);
        return response;
    }
}

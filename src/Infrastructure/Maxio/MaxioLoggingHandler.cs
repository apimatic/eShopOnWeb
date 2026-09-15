using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioLoggingHandler> _logger;

    public MaxioLoggingHandler(ILogger<MaxioLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.GetLeftPart(System.UriPartial.Path);
        _logger.LogInformation("Maxio {Method} {Path}", request.Method, path);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Maxio {Method} {Path} -> {StatusCode}", request.Method, path, (int)response.StatusCode);
        return response;
    }
}

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioLoggingHandler : DelegatingHandler
{
    private readonly ILogger<TwilioLoggingHandler> _logger;

    public TwilioLoggingHandler(ILogger<TwilioLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.GetLeftPart(System.UriPartial.Path);
        _logger.LogInformation("Twilio {Method} {Path}", request.Method, path);
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Twilio {Method} {Path} -> {StatusCode}", request.Method, path, (int)response.StatusCode);
        return response;
    }
}

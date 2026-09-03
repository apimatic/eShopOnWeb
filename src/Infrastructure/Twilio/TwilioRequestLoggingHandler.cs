using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<TwilioRequestLoggingHandler> _logger;

    public TwilioRequestLoggingHandler(ILogger<TwilioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var sanitized = Regex.Replace(path, @"AC[0-9a-fA-F]{32}", "AC***");
        sanitized = Regex.Replace(sanitized, @"MG[0-9a-fA-F]{32}", "MG***");
        sanitized = Regex.Replace(sanitized, @"SM[0-9a-fA-F]{32}", "SM***");
        sanitized = Regex.Replace(sanitized, @"MM[0-9a-fA-F]{32}", "MM***");
        sanitized = Regex.Replace(sanitized, @"\+\d{8,15}", "[num]");
        sanitized = Regex.Replace(sanitized, @"%2B\d{8,15}", "[num]");
        _logger.LogInformation("Twilio {Method} {Host}{Path} -> {Status}", request.Method, request.RequestUri?.Host, sanitized, (int)response.StatusCode);
        return response;
    }
}

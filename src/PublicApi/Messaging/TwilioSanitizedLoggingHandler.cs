using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioSanitizedLoggingHandler : DelegatingHandler
{
    private static readonly Regex PhoneSegment = new(
        @"/PhoneNumbers/[^/]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<TwilioSanitizedLoggingHandler> _logger;

    public TwilioSanitizedLoggingHandler(ILogger<TwilioSanitizedLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? "unknown";
        var path = PhoneSegment.Replace(request.RequestUri?.AbsolutePath ?? string.Empty, "/PhoneNumbers/[redacted]");
        _logger.LogInformation("Twilio {Method} {Host}{Path}", request.Method, host, path);

        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Twilio response {StatusCode} {Method} {Host}{Path}", (int)response.StatusCode, request.Method, host, path);
        return response;
    }
}

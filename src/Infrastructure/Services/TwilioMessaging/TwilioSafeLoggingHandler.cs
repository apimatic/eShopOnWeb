using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.TwilioMessaging;

internal sealed class TwilioSafeLoggingHandler : DelegatingHandler
{
    private readonly ILogger<TwilioSafeLoggingHandler> _logger;

    public TwilioSafeLoggingHandler(ILogger<TwilioSafeLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = RedactPath(request.RequestUri);
        _logger.LogInformation("Twilio {Method} {Path}", request.Method, path);

        var response = await base.SendAsync(request, cancellationToken);

        _logger.LogInformation("Twilio {Method} {Path} -> {StatusCode}", request.Method, path, (int)response.StatusCode);
        return response;
    }

    private static string RedactPath(Uri? uri)
    {
        if (uri is null)
        {
            return "(none)";
        }

        var path = uri.AbsolutePath;
        const string phoneSegment = "/PhoneNumbers/";
        var index = path.IndexOf(phoneSegment, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return path[..(index + phoneSegment.Length)] + "{redacted}";
        }

        return path;
    }
}

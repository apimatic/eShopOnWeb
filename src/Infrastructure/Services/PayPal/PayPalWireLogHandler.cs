using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Logs the method, path and response status of every PayPal call so the outbound request count and
/// outcomes are observable. It deliberately never logs request or response bodies — those carry card
/// data — so nothing sensitive is written to logs.
/// </summary>
public sealed class PayPalWireLogHandler : DelegatingHandler
{
    private readonly ILogger<PayPalWireLogHandler> _logger;

    public PayPalWireLogHandler(ILogger<PayPalWireLogHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        _logger.LogInformation("PayPal --> {Method} {Path}", request.Method, path);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _logger.LogInformation("PayPal <-- {Status} {Method} {Path}", (int)response.StatusCode, request.Method, path);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PayPal <-- transport failure for {Method} {Path}: {Error}", request.Method, path, ex.GetType().Name);
            throw;
        }
    }
}

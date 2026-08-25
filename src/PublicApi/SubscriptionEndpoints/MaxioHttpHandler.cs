using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioHttpHandler : DelegatingHandler
{
    private readonly MaxioRequestContext _context;
    private readonly ILogger<MaxioHttpHandler> _logger;

    public MaxioHttpHandler(MaxioRequestContext context, ILogger<MaxioHttpHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !_context.TryBeginWrite())
        {
            throw new MaxioWriteAlreadyAttemptedException();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            _context.RecordStatus(response.StatusCode);
            _logger.LogInformation(
                "Maxio {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is not MaxioWriteAlreadyAttemptedException)
        {
            _logger.LogWarning(
                "Maxio {Method} {Path} failed after {ElapsedMilliseconds} ms with {ExceptionType}",
                request.Method.Method,
                request.RequestUri?.AbsolutePath,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name);
            throw;
        }
    }
}

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses any resend of a request issued inside a <see cref="MaxioWriteScope"/>, so a
/// non-idempotent write reaches the billing provider at most once. Requests outside a write scope
/// - every read - are untouched and retry normally.
/// </summary>
internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private readonly ILogger<MaxioWriteOnceHandler> _logger;

    public MaxioWriteOnceHandler(ILogger<MaxioWriteOnceHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!MaxioWriteScope.TryClaimSend())
        {
            _logger.LogWarning(
                "Blocked an automatic resend of {Method} {Path}: the write had already been sent once and must not be duplicated.",
                request.Method,
                request.RequestUri?.AbsolutePath);

            throw new MaxioWriteResendBlockedException(
                "A billing write was already sent once; the automatic resend was blocked.");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

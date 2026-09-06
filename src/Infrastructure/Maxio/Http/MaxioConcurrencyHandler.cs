using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>Holds every outbound Maxio call behind the shared concurrency limiter.</summary>
public sealed class MaxioConcurrencyHandler : DelegatingHandler
{
    private readonly MaxioConcurrencyLimiter _limiter;

    public MaxioConcurrencyHandler(MaxioConcurrencyLimiter limiter)
    {
        _limiter = limiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _limiter.Semaphore.WaitAsync(cancellationToken);
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _limiter.Semaphore.Release();
        }
    }
}

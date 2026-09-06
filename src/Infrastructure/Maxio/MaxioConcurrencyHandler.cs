using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Caps the number of in-flight requests this process sends to Maxio.
/// <para>
/// Maxio limits a site to a small number of concurrent API calls and queues the excess, so firing
/// more concurrent requests makes everything slower and risks throttling. Shaping the load here
/// keeps us inside that budget no matter how many shoppers hit the endpoints at once.
/// </para>
/// </summary>
public class MaxioConcurrencyHandler : DelegatingHandler
{
    private readonly MaxioConcurrencyLimiter _limiter;

    public MaxioConcurrencyHandler(MaxioConcurrencyLimiter limiter)
    {
        _limiter = limiter;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _limiter.Gate.WaitAsync(cancellationToken);
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _limiter.Gate.Release();
        }
    }
}

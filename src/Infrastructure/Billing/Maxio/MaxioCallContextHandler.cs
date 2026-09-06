using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Enforces the write-once guard and records the response status for the ambient
/// <see cref="MaxioCallContext"/>. Requests made outside a context pass straight through.
/// </summary>
internal sealed class MaxioCallContextHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = MaxioCallContext.Current;
        if (context is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Count the send BEFORE it goes out: a request that failed on the way out may still have
        // been received, so "this may already have taken effect" is the only safe reading.
        var attempt = context.RegisterSend();
        if (context.WriteOnce && attempt > 1)
        {
            throw new MaxioResendBlockedException(
                "Refused to re-send " + request.Method + " " + request.RequestUri?.AbsolutePath +
                " - the first attempt may already have been received.");
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        context.RecordResponse(response.StatusCode);
        return response;
    }
}

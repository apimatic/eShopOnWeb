using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !TwilioWriteOnce.TryAcquirePost())
        {
            throw new TwilioWriteOnceViolationException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioTransportHandler : DelegatingHandler
{
    private readonly MaxioCallContext _callContext;

    public MaxioTransportHandler(MaxioCallContext callContext) => _callContext = callContext;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = _callContext.Current;
        if (state?.WriteOnce == true && request.Method == HttpMethod.Post &&
            Interlocked.Increment(ref state.PostSendCount) > 1)
        {
            throw new MaxioWriteReplayPreventedException();
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (state is not null)
        {
            state.LastStatusCode = response.StatusCode;
        }

        return response;
    }
}

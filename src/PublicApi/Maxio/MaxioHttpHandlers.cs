using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private readonly IMaxioAttemptContext _attemptContext;

    public MaxioWriteOnceHandler(IMaxioAttemptContext attemptContext) => _attemptContext = attemptContext;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_attemptContext.TryBeginSend(request.Method))
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MaxioResponseStatusHandler : DelegatingHandler
{
    private readonly IMaxioAttemptContext _attemptContext;

    public MaxioResponseStatusHandler(IMaxioAttemptContext attemptContext) => _attemptContext = attemptContext;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        _attemptContext.RecordResponse(response.StatusCode);
        return response;
    }
}

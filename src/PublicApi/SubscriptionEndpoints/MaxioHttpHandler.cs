using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioHttpHandler : DelegatingHandler
{
    private readonly MaxioRequestContext _requestContext;
    private readonly ILogger<MaxioHttpHandler> _logger;

    public MaxioHttpHandler(
        MaxioRequestContext requestContext,
        ILogger<MaxioHttpHandler> logger)
    {
        _requestContext = requestContext;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_requestContext.TryBeginSend(request.Method == HttpMethod.Post))
        {
            throw new MaxioWriteResendBlockedException();
        }

        _logger.LogDebug("Sending Maxio {Method} {Path}", request.Method, request.RequestUri?.AbsolutePath);
        var response = await base.SendAsync(request, cancellationToken);
        _requestContext.Record(response.StatusCode);
        _logger.LogDebug("Maxio returned HTTP {StatusCode}", (int)response.StatusCode);
        return response;
    }
}

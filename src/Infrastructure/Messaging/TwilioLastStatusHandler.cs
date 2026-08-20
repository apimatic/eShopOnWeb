using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioCallContext
{
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public static HttpStatusCode? LastStatusCode
    {
        get => LastStatus.Value;
        set => LastStatus.Value = value;
    }
}

internal sealed class TwilioLastStatusHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        TwilioCallContext.LastStatusCode = response.StatusCode;
        return response;
    }
}

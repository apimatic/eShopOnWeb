using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioLastHttp
{
    public static readonly AsyncLocal<HttpStatusCode?> Status = new();
}

internal static class MaxioWriteOnce
{
    private static readonly AsyncLocal<bool> Armed = new();
    private static readonly AsyncLocal<int> Sends = new();

    public static IDisposable Arm()
    {
        Armed.Value = true;
        Sends.Value = 0;
        return new Lease();
    }

    public static void CountOrThrow()
    {
        if (!Armed.Value)
        {
            return;
        }

        if (Sends.Value >= 1)
        {
            throw new MaxioDuplicateSendException();
        }

        Sends.Value++;
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose()
        {
            Armed.Value = false;
            Sends.Value = 0;
        }
    }
}

internal sealed class MaxioDuplicateSendException : Exception
{
    public MaxioDuplicateSendException()
        : base("A duplicate billing write was blocked.")
    {
    }
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastHttp.Status.Value = response.StatusCode;
        return response;
    }
}

internal sealed class MaxioSingleSendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete)
        {
            MaxioWriteOnce.CountOrThrow();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException()
        : base("A non-idempotent PayPal write was blocked from being sent a second time.")
    {
    }
}

internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int> SendCount = new();

    public static void Reset() => SendCount.Value = 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsIdempotent(request.Method))
        {
            var count = SendCount.Value + 1;
            SendCount.Value = count;
            if (count > 1)
            {
                throw new PayPalDuplicateSendException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsIdempotent(HttpMethod method)
    {
        return method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Put
            || method == HttpMethod.Options;
    }
}

internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatus => Last.Value;

    public static void Reset() => Last.Value = null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Last.Value = response.StatusCode;
        return response;
    }
}

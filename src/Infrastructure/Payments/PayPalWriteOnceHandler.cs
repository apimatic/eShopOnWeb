using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalCallContext
{
    public static readonly AsyncLocal<int?> LastStatusCode = new();
    public static readonly AsyncLocal<bool> ProtectWrites = new();
    public static readonly AsyncLocal<int> WriteCount = new();
}

internal sealed class DuplicatePayPalWriteException : Exception
{
    public DuplicatePayPalWriteException() : base("A duplicate PayPal write was blocked.") { }
}

internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        PayPalCallContext.LastStatusCode.Value = null;

        if (PayPalCallContext.ProtectWrites.Value && IsProtectedWrite(request))
        {
            var count = PayPalCallContext.WriteCount.Value + 1;
            PayPalCallContext.WriteCount.Value = count;
            if (count > 1)
            {
                throw new DuplicatePayPalWriteException();
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        PayPalCallContext.LastStatusCode.Value = (int)response.StatusCode;
        return response;
    }

    private static bool IsProtectedWrite(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post && request.Method != HttpMethod.Delete)
        {
            return false;
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        return !path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
    }
}

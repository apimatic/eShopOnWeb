using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class DuplicateSendRefusedException : Exception
{
    public DuplicateSendRefusedException()
        : base("A retry attempted to resend a PayPal write that had already left this process.")
    {
    }
}

internal sealed class SingleSendGuard
{
    private static readonly AsyncLocal<int> SendCount = new();
    private static readonly AsyncLocal<bool> Write = new();

    public static void BeginWrite()
    {
        Write.Value = true;
        SendCount.Value = 0;
    }

    public static void EndWrite()
    {
        Write.Value = false;
        SendCount.Value = 0;
    }

    public static void CountOrRefuse()
    {
        if (!Write.Value)
        {
            return;
        }

        var next = SendCount.Value + 1;
        SendCount.Value = next;
        if (next > 1)
        {
            throw new DuplicateSendRefusedException();
        }
    }
}

internal sealed class RefuseUnauthorizedResendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SingleSendGuard.CountOrRefuse();
        return base.SendAsync(request, cancellationToken);
    }
}

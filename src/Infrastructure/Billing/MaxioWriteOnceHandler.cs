using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioWriteGate
{
    private static readonly AsyncLocal<int> Sends = new();

    public static void BeginWrite() => Sends.Value = 0;

    public static void CountOrReject(HttpMethod method)
    {
        if (!HttpMethod.Post.Equals(method) && !HttpMethod.Patch.Equals(method) && !HttpMethod.Delete.Equals(method))
        {
            return;
        }

        var next = Sends.Value + 1;
        Sends.Value = next;
        if (next > 1)
        {
            throw new MaxioDuplicateWriteException();
        }
    }
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        MaxioWriteGate.CountOrReject(request.Method);
        return base.SendAsync(request, cancellationToken);
    }
}

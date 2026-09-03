using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class SingleAttemptWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int> SendCount = new();

    public static void BeginWrite() => SendCount.Value = 0;

    public static void EndWrite() => SendCount.Value = 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            if (SendCount.Value >= 1)
            {
                throw new DuplicateProviderWriteException();
            }

            SendCount.Value++;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;
}

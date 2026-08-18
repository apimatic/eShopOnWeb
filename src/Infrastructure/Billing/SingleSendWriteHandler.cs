using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Counts POST attempts inside an explicit write scope so the SDK's transport retry cannot resend a write.
/// The count lives in <see cref="AsyncLocal{T}"/>, not on <see cref="HttpRequestMessage"/> — retries build a new request.
/// </summary>
internal sealed class SingleSendWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> SendCount = new();

    public static IDisposable BeginWriteScope()
    {
        SendCount.Value = 0;
        return new ScopeReset();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && SendCount.Value.HasValue)
        {
            if (SendCount.Value >= 1)
            {
                throw new DuplicateWritePreventedException();
            }

            SendCount.Value = 1;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class ScopeReset : IDisposable
    {
        public void Dispose() => SendCount.Value = null;
    }
}

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class DuplicateOutboundPostException : Exception
{
    public DuplicateOutboundPostException()
        : base("A duplicate POST to the billing provider was blocked.")
    {
    }
}

internal sealed class PreventRetryPostHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int> PostCount = new();

    internal static void BeginWrite() => PostCount.Value = 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            var count = PostCount.Value + 1;
            PostCount.Value = count;
            if (count > 1)
            {
                throw new DuplicateOutboundPostException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}

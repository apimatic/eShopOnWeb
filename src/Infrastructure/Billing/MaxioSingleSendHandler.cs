using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class DuplicateWriteRejectedException : Exception
{
    public DuplicateWriteRejectedException()
        : base("A billing write was not resent after the first attempt.")
    {
    }
}

internal sealed class MaxioSingleSendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (MaxioCallContext.IsWrite && IsWrite(request.Method) && MaxioCallContext.IncrementSendCount() > 1)
        {
            throw new DuplicateWriteRejectedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method)
        => method == HttpMethod.Post
           || method == HttpMethod.Patch
           || method == HttpMethod.Delete
           || method == HttpMethod.Put;
}

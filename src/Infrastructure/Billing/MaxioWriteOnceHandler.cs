using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// SDK transport retries resend every verb, including POST. This handler refuses a second
/// write in the caller's <see cref="AsyncLocal{T}"/> scope so a duplicate enrollment cannot
/// reach Maxio. Throw a non-<see cref="HttpRequestException"/> sentinel so the retry pipeline
/// does not retry the refusal itself.
/// </summary>
internal sealed class MaxioDuplicateWriteException : Exception
{
    public MaxioDuplicateWriteException()
        : base("A billing write was not resent after a transport failure.")
    {
    }
}

internal static class MaxioWriteGuard
{
    private static readonly AsyncLocal<int> SendCount = new();

    public static void Reset() => SendCount.Value = 0;

    public static int Increment() => ++SendCount.Value;
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && MaxioWriteGuard.Increment() > 1)
        {
            throw new MaxioDuplicateWriteException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete;
}

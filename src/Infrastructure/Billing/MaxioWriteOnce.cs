using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// SDK transport retries resend POST on <see cref="HttpRequestException"/>. This scope + handler
/// refuse a second write send; the marker lives in <see cref="AsyncLocal{T}"/> so it survives
/// the fresh <see cref="HttpRequestMessage"/> built for each retry attempt.
/// </summary>
internal static class MaxioWriteOnceScope
{
    private static readonly AsyncLocal<bool> Active = new();
    private static readonly AsyncLocal<int> WriteSends = new();

    public static IDisposable Begin()
    {
        Active.Value = true;
        WriteSends.Value = 0;
        return new Reset();
    }

    public static bool TryRegisterWrite()
    {
        if (!Active.Value)
        {
            return true;
        }

        var next = WriteSends.Value + 1;
        WriteSends.Value = next;
        return next == 1;
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose()
        {
            Active.Value = false;
            WriteSends.Value = 0;
        }
    }
}

/// <summary>
/// Sentinel that must not derive from <see cref="HttpRequestException"/> (that type is retried).
/// </summary>
internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A retried billing write was blocked after the first attempt.")
    {
    }
}

internal sealed class SingleAttemptWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && !MaxioWriteOnceScope.TryRegisterWrite())
        {
            throw new DuplicateProviderWriteException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        HttpMethod.Post.Equals(method) || HttpMethod.Patch.Equals(method) || HttpMethod.Put.Equals(method) ||
        HttpMethod.Delete.Equals(method);
}

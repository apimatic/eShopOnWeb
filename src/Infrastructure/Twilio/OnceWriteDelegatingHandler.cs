using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class DuplicateTwilioWriteException : Exception
{
    public DuplicateTwilioWriteException()
        : base("A duplicate Twilio write was blocked.")
    {
    }
}

internal sealed class OnceWriteDelegatingHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteGuard?> Guard = new();

    private sealed class WriteGuard
    {
        public int PostCount;
    }

    public static IDisposable Begin()
    {
        Guard.Value = new WriteGuard();
        return new Reset();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Guard.Value is { } guard && request.Method == HttpMethod.Post)
        {
            var count = Interlocked.Increment(ref guard.PostCount);
            if (count > 1)
            {
                throw new DuplicateTwilioWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose()
        {
            Guard.Value = null;
        }
    }
}

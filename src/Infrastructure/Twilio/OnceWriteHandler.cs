using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioWriteGuard
{
    private static readonly AsyncLocal<WriteState?> State = new();

    public static IDisposable Begin()
    {
        State.Value = new WriteState();
        return new Scope();
    }

    public static void AuthorizeOneWrite()
    {
        var state = State.Value;
        if (state is null)
        {
            return;
        }

        if (Interlocked.Increment(ref state.Count) > 1)
        {
            throw new DuplicateWritePreventedException();
        }
    }

    private sealed class WriteState
    {
        public int Count;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => State.Value = null;
    }
}

public sealed class OnceWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete)
        {
            TwilioWriteGuard.AuthorizeOneWrite();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

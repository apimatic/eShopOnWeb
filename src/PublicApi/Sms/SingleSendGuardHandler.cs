using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// Refusal to send a message-create request more than once within a single logical send.
///
/// The SDK's retry pipeline resends a request on a transport failure (a dropped socket, a reset)
/// on <em>any</em> verb — and a reset thrown after the bytes reached the provider is
/// indistinguishable from one thrown before, so a naive retry can create a second, real, paid-for
/// SMS. This handler holds the count at one: the create only reaches the network once; a retry of
/// it is refused before it goes out, so the outcome of the one allowed send is unknown (settle it
/// by reading provider state) rather than doubled.
///
/// Two details make it correct (per the resilience guidance): the "already sent" marker lives in an
/// <see cref="AsyncLocal{T}"/> scope that outlives the per-attempt <see cref="HttpRequestMessage"/>
/// (a fresh request is built for each attempt), and the refusal is a private sentinel that does NOT
/// derive from <see cref="HttpRequestException"/> — otherwise the refusal itself would be retried.
/// </summary>
internal sealed class SingleSendGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<StrongBox<int>?> Counter = new();

    /// <summary>
    /// Open a single-send scope around one logical create call. Message-create requests issued
    /// inside the returned scope are allowed to reach the network exactly once.
    /// </summary>
    public static IDisposable BeginSingleSend()
    {
        Counter.Value = new StrongBox<int>(0);
        return new Scope();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var box = Counter.Value;
        if (box is not null && IsMessageCreate(request))
        {
            // Count BEFORE it goes out: a request that failed on the way out may still have been received.
            if (Interlocked.Increment(ref box.Value) > 1)
            {
                throw new DuplicateSendRefusedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    // Message creation is POST .../Messages.json. Update/redact/cancel target .../Messages/{Sid}.json,
    // fetch/list are GET — none of those match, so the guard only ever gates a create.
    private static bool IsMessageCreate(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post
        && request.RequestUri is not null
        && request.RequestUri.AbsolutePath.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase);

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Counter.Value = null;
    }
}

/// <summary>
/// Sentinel raised when a message-create is retried within a single-send scope. Deliberately not an
/// <see cref="HttpRequestException"/>, so the SDK retry pipeline does not treat the refusal as a
/// retryable transport failure.
/// </summary>
internal sealed class DuplicateSendRefusedException : Exception
{
    public DuplicateSendRefusedException()
        : base("A duplicate message-create was refused after a transport failure; the outcome of the first send is unknown.")
    {
    }
}

using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when the guard refuses to let a send POST be re-sent. It deliberately does NOT derive from
/// <see cref="HttpRequestException"/> — that is the type the SDK's retry pipeline retries, so a refusal
/// thrown as one would itself become retryable.
/// </summary>
internal sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A send was refused because it had already been attempted in this scope.") { }
}

/// <summary>
/// Guarantees a create-message POST reaches the provider at most once per logical send, even though the
/// SDK's retry layer re-sends a POST on a transport failure (a connection reset thrown after the bytes
/// left is indistinguishable from one thrown before). The "already attempted" flag lives in an
/// <see cref="AsyncLocal{T}"/> scope the gateway opens around the send; retries run inside that same async
/// context, so the flag flows into the handler on every attempt.
/// </summary>
internal static class SingleSendGuard
{
    private static readonly AsyncLocal<StrongBox<bool>?> Current = new();

    /// <summary>Open a "send at most once" scope around a create-message call. Dispose to close it.</summary>
    public static IDisposable Begin()
    {
        var previous = Current.Value;
        Current.Value = new StrongBox<bool>(false);
        return new Scope(previous);
    }

    /// <summary>Called by the handler: returns true if the POST may proceed, false if it is a blocked re-send.</summary>
    public static bool TryClaim()
    {
        var box = Current.Value;
        if (box is null)
        {
            return true; // no scope active (not a guarded send) — allow.
        }

        if (box.Value)
        {
            return false; // already claimed once in this scope — this is a re-send.
        }

        box.Value = true;
        return true;
    }

    private sealed class Scope : IDisposable
    {
        private readonly StrongBox<bool>? _previous;
        public Scope(StrongBox<bool>? previous) => _previous = previous;
        public void Dispose() => Current.Value = _previous;
    }
}

/// <summary>
/// The HTTP handler half of <see cref="SingleSendGuard"/>. It counts each POST attempt that runs inside an
/// open guard scope and blocks the second, so a create-message send cannot be silently duplicated by a
/// transport-failure retry. Non-guarded traffic (reads, updates outside a scope) passes straight through.
/// </summary>
internal sealed class SingleSendGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Post && !SingleSendGuard.TryClaim())
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, ct);
    }
}

using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Ambient marker around a single Maxio write, opened by the calling code and read by
/// <see cref="MaxioWriteOnceHandler"/> further down the pipeline.
/// <para>
/// It lives on an <see cref="AsyncLocal{T}"/> rather than on the request message because the SDK builds a
/// <b>fresh</b> <see cref="System.Net.Http.HttpRequestMessage"/> for every attempt - a marker carried on the
/// request is gone by the time a resend happens. Retries run inside the caller's async context, so the
/// scope does flow into the handler on every attempt.
/// </para>
/// <para>
/// It carries two things: the send count, which enforces at-most-one write, and the HTTP status actually
/// observed, which is the only way to tell a rejection from an unreadable success when deserializing the
/// response throws and destroys the SDK exception that would otherwise have carried that status.
/// </para>
/// </summary>
internal sealed class MaxioWriteScope : IDisposable
{
    private static readonly AsyncLocal<MaxioWriteScope?> Ambient = new();

    private readonly MaxioWriteScope? _previous;
    private int _sendCount;

    public MaxioWriteScope(string operation)
    {
        Operation = operation;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static MaxioWriteScope? Current => Ambient.Value;

    public string Operation { get; }

    /// <summary>How many times a write left this process inside the scope. Never more than one is allowed.</summary>
    public int SendCount => Volatile.Read(ref _sendCount);

    /// <summary>Status of the single response observed, or null when no response came back at all.</summary>
    public int? ObservedStatusCode { get; private set; }

    /// <summary>
    /// Claims the one permitted send. Counted <b>before</b> the request goes out: a request that failed on
    /// the way out may still have been received, so a second attempt can never be assumed harmless.
    /// </summary>
    public bool TryClaimSend() => Interlocked.Increment(ref _sendCount) == 1;

    public void RecordResponse(int statusCode) => ObservedStatusCode = statusCode;

    public void Dispose() => Ambient.Value = _previous;
}

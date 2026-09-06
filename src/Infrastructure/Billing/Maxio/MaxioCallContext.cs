using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Ambient state for one logical Maxio operation, flowed to <see cref="MaxioCallContextHandler"/>
/// through the async context so it survives the SDK's internal retry pipeline.
/// <para>
/// It exists for two reasons the SDK cannot serve on its own:
/// </para>
/// <list type="number">
///   <item>
///     A transport failure is resent by the SDK on <em>every</em> verb, so a POST can reach Maxio
///     more than once. A context opened with <see cref="BeginWrite"/> refuses the second send, which
///     is the only way to hold the number of enrollments actually delivered at one.
///   </item>
///   <item>
///     When a non-2xx body does not match the SDK's generated error model, the SDK throws a
///     <c>JsonException</c> and the HTTP status is destroyed with it. Recording the status here keeps
///     a deterministic rejection from being reported as a transient outage.
///   </item>
/// </list>
/// The marker deliberately does not live on the <c>HttpRequestMessage</c>: a fresh request object is
/// built for every attempt, so a per-request marker would be gone by the time the retry runs.
/// </summary>
internal sealed class MaxioCallContext : IDisposable
{
    private static readonly AsyncLocal<MaxioCallContext?> Ambient = new();

    private readonly MaxioCallContext? _previous;
    private int _sendCount;
    private int _lastStatusCode;

    private MaxioCallContext(bool writeOnce)
    {
        WriteOnce = writeOnce;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static MaxioCallContext? Current => Ambient.Value;

    /// <summary>Opens a context for a read. Retries are left alone; the status is still recorded.</summary>
    public static MaxioCallContext BeginRead() => new(writeOnce: false);

    /// <summary>Opens a context for a write. Any send after the first is refused.</summary>
    public static MaxioCallContext BeginWrite() => new(writeOnce: true);

    public bool WriteOnce { get; }

    /// <summary>Number of requests handed to the network for this operation.</summary>
    public int SendCount => Volatile.Read(ref _sendCount);

    /// <summary>Status of the most recent response, or null when no response was ever received.</summary>
    public HttpStatusCode? LastStatusCode
    {
        get
        {
            var code = Volatile.Read(ref _lastStatusCode);
            return code == 0 ? null : (HttpStatusCode)code;
        }
    }

    /// <summary>True when a response arrived and it carried a success status.</summary>
    public bool LastResponseWasSuccess =>
        LastStatusCode is { } code && (int)code >= 200 && (int)code <= 299;

    internal int RegisterSend() => Interlocked.Increment(ref _sendCount);

    internal void RecordResponse(HttpStatusCode statusCode) =>
        Volatile.Write(ref _lastStatusCode, (int)statusCode);

    public void Dispose() => Ambient.Value = _previous;
}

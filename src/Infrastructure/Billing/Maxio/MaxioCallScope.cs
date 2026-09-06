using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Ambient, per-logical-call state shared with <see cref="MaxioCallTrackingHandler"/>.
/// <para>
/// It exists because the SDK's retry pipeline sits above <c>HttpClient.SendAsync</c> and builds a fresh
/// <see cref="System.Net.Http.HttpRequestMessage"/> for every attempt, so anything stashed on the request
/// is gone by the retry. Retries run inside the caller's async context, so an <see cref="AsyncLocal{T}"/>
/// opened around the call does flow into the handler on every attempt.
/// </para>
/// <para>It carries two things the SDK does not otherwise give us:</para>
/// <list type="bullet">
/// <item><description>
/// a send counter, so a write marked <see cref="IsWriteOnce"/> can be held at exactly one delivery even
/// though the SDK re-sends any verb on a transport failure and retries cannot be disabled;
/// </description></item>
/// <item><description>
/// the last observed HTTP status, so an error body that fails to deserialize — which destroys the
/// <c>SdkException</c> and the status with it — can still be attributed to the status it arrived with.
/// </description></item>
/// </list>
/// </summary>
internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<MaxioCallScope?> Ambient = new();

    private readonly MaxioCallScope? _previous;
    private int _sends;
    private int _lastStatusCode;
    private bool _disposed;

    private MaxioCallScope(string operation, bool isWriteOnce)
    {
        Operation = operation;
        IsWriteOnce = isWriteOnce;
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static MaxioCallScope? Current => Ambient.Value;

    public string Operation { get; }

    /// <summary>When true, the tracking handler refuses any send after the first.</summary>
    public bool IsWriteOnce { get; }

    /// <summary>Number of requests handed to the network for this logical call, including refused ones.</summary>
    public int Sends => Volatile.Read(ref _sends);

    /// <summary>The status of the last response observed on the wire, or <c>null</c> if none arrived.</summary>
    public HttpStatusCode? LastStatusCode
    {
        get
        {
            var code = Volatile.Read(ref _lastStatusCode);
            return code == 0 ? null : (HttpStatusCode)code;
        }
    }

    /// <summary>Opens a scope for a read. Retries are left to the SDK.</summary>
    public static MaxioCallScope BeginRead(string operation) => new(operation, isWriteOnce: false);

    /// <summary>
    /// Opens a scope for a write that must reach Maxio at most once. A refused re-send never reaches the
    /// network, so the write is genuinely delivered once — but a refusal means the outcome of the one
    /// delivery is unknown and must be settled by re-reading Maxio state.
    /// </summary>
    public static MaxioCallScope BeginWriteOnce(string operation) => new(operation, isWriteOnce: true);

    /// <summary>Counted before the request goes out: a request that failed on the way out may still have been received.</summary>
    internal bool TryRegisterSend() => Interlocked.Increment(ref _sends) == 1 || !IsWriteOnce;

    internal void RecordStatus(HttpStatusCode statusCode) => Volatile.Write(ref _lastStatusCode, (int)statusCode);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Ambient.Value = _previous;
    }
}

/// <summary>
/// Thrown by <see cref="MaxioCallTrackingHandler"/> to refuse a re-send of a write-once request.
/// <para>
/// Deliberately not an <see cref="System.Net.Http.HttpRequestException"/>: that is the very type the SDK's
/// retry pipeline retries, so refusing with one would make the refusal itself retryable.
/// </para>
/// </summary>
internal sealed class MaxioResendBlockedException : Exception
{
    public MaxioResendBlockedException(string operation)
        : base($"Refused to re-send the Maxio write '{operation}'. The first attempt may already have taken effect.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}

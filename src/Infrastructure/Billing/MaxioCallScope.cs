using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Ambient state for one logical Maxio call, flowed to <see cref="MaxioHttpDiagnosticsHandler"/> so that the
/// handler can enforce write-once semantics and record the HTTP status the SDK is about to discard.
/// </summary>
/// <remarks>
/// The scope deliberately lives in an <see cref="AsyncLocal{T}"/> rather than on the
/// <see cref="System.Net.Http.HttpRequestMessage"/>: the SDK's retry pipeline builds a fresh request object
/// for each attempt, so anything attached to the request is gone by the time a retry runs. Retries do execute
/// inside the caller's async context, so the scope flows into the handler on every attempt.
/// </remarks>
internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<MaxioCallScope?> CurrentScope = new();

    private readonly MaxioCallScope? _previous;
    private bool _disposed;

    /// <summary>Number of times the request has actually been handed to the transport.</summary>
    public int Sends;

    private MaxioCallScope(bool writeOnce, MaxioCallScope? previous)
    {
        WriteOnce = writeOnce;
        _previous = previous;
    }

    public static MaxioCallScope? Current => CurrentScope.Value;

    /// <summary>When true, the handler refuses any send after the first.</summary>
    public bool WriteOnce { get; }

    /// <summary>The status of the most recent response the transport saw, or null if none arrived.</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>Opens a scope. <paramref name="writeOnce"/> guarantees at most one send reaches the network.</summary>
    public static MaxioCallScope Begin(bool writeOnce)
    {
        var scope = new MaxioCallScope(writeOnce, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentScope.Value = _previous;
    }
}

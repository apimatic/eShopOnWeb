using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Arms a one-send gate for a single non-idempotent write. Transport retries that rebuild
/// the HttpRequestMessage still share this AsyncLocal scope, so a second POST is refused.
/// </summary>
internal static class OncePerWriteGate
{
    private static readonly AsyncLocal<GateState?> State = new();

    public static IDisposable Begin()
    {
        var previous = State.Value;
        State.Value = new GateState();
        return new Popper(previous);
    }

    public static void CountOrThrow()
    {
        var state = State.Value;
        if (state is null)
        {
            return;
        }

        if (state.Count >= 1)
        {
            throw new DuplicateWriteRefusedException();
        }

        state.Count++;
    }

    private sealed class GateState
    {
        public int Count { get; set; }
    }

    private sealed class Popper : IDisposable
    {
        private readonly GateState? _previous;
        private bool _disposed;

        public Popper(GateState? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            State.Value = _previous;
        }
    }
}

internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A duplicate write was refused before it reached the billing provider.")
    {
    }
}

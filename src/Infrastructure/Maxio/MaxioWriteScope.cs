using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Prevents the SDK retry pipeline from resending a subscription creation after a transport fault.
/// The provider reference is reconciled before a later attempt may issue another write.
/// </summary>
public sealed class MaxioWriteScope
{
    private readonly AsyncLocal<ScopeState?> _current = new();

    public string? CurrentReference => _current.Value?.Reference;

    public bool TryMarkSent()
    {
        var state = _current.Value;
        if (state is null || state.Sent)
        {
            return false;
        }

        state.Sent = true;
        return true;
    }

    public IDisposable Begin(string reference)
    {
        var previous = _current.Value;
        _current.Value = new ScopeState(reference);
        return new Scope(this, previous);
    }

    private sealed class ScopeState(string reference)
    {
        public string Reference { get; } = reference;
        public bool Sent { get; set; }
    }

    private sealed class Scope(MaxioWriteScope owner, ScopeState? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException() : base("A Maxio subscription write retry was blocked pending reconciliation.")
    {
    }
}

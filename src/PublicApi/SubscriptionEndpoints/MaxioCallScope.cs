using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<CallState?> CurrentState = new();
    private readonly CallState? _parent;

    private MaxioCallScope(bool writeOnce)
    {
        _parent = CurrentState.Value;
        State = new CallState(writeOnce);
        CurrentState.Value = State;
    }

    internal CallState State { get; }
    internal static CallState? Current => CurrentState.Value;

    internal static MaxioCallScope Begin(bool writeOnce) => new(writeOnce);

    public void Dispose()
    {
        CurrentState.Value = _parent;
    }

    internal sealed class CallState
    {
        private int _writeSends;

        internal CallState(bool writeOnce)
        {
            WriteOnce = writeOnce;
        }

        internal bool WriteOnce { get; }
        internal HttpStatusCode? LastStatusCode { get; set; }
        internal int RecordWriteSend() => Interlocked.Increment(ref _writeSends);
    }
}

internal sealed class MaxioWriteReplayBlockedException : Exception
{
    internal MaxioWriteReplayBlockedException()
        : base("A provider write retry was blocked because its outcome is ambiguous.")
    {
    }
}

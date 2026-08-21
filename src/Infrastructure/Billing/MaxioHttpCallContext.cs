using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioHttpCallContext
{
    private static readonly AsyncLocal<CallState?> State = new();

    public static CallState? Current => State.Value;

    public static IDisposable BeginScope()
    {
        var prior = State.Value;
        State.Value = new CallState();
        return new Popper(prior);
    }

    public sealed class CallState
    {
        public int WriteSends { get; set; }
        public System.Net.HttpStatusCode? LastStatus { get; set; }
    }

    private sealed class Popper : IDisposable
    {
        private readonly CallState? _prior;
        private bool _disposed;

        public Popper(CallState? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            State.Value = _prior;
        }
    }
}

internal sealed class MaxioWriteResendRefusedException : Exception
{
    public MaxioWriteResendRefusedException()
        : base("A non-idempotent billing write was not resent.")
    {
    }
}

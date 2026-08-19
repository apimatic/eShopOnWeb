using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A duplicate billing write was blocked after the first attempt.")
    {
    }
}

internal static class OnceOnlyWriteScope
{
    private static readonly AsyncLocal<WriteGuard?> CurrentGuard = new();

    public static WriteGuard? Current => CurrentGuard.Value;

    public static IDisposable Begin()
    {
        var previous = CurrentGuard.Value;
        var guard = new WriteGuard();
        CurrentGuard.Value = guard;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly WriteGuard? _previous;
        private bool _disposed;

        public Scope(WriteGuard? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentGuard.Value = _previous;
        }
    }
}

internal sealed class WriteGuard
{
    public bool Sent { get; set; }
}

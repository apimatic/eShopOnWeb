using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioCallContext
{
    private static readonly AsyncLocal<CallState?> CurrentState = new();

    public static HttpStatusCode? LastStatusCode => CurrentState.Value?.LastStatusCode;

    public static IDisposable Begin()
    {
        var previous = CurrentState.Value;
        CurrentState.Value = new CallState();
        return new Scope(previous);
    }

    public static void Record(HttpStatusCode statusCode)
    {
        if (CurrentState.Value is not null)
        {
            CurrentState.Value.LastStatusCode = statusCode;
        }
    }

    private sealed class CallState
    {
        public HttpStatusCode? LastStatusCode { get; set; }
    }

    private sealed class Scope(CallState? previous) : IDisposable
    {
        public void Dispose() => CurrentState.Value = previous;
    }
}

using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioCallContext
{
    private static readonly AsyncLocal<CallState?> State = new();

    public static void Begin()
    {
        State.Value = new CallState();
    }

    public static IDisposable BeginWrite()
    {
        var call = new CallState { IsWrite = true };
        State.Value = call;
        return new Resetter();
    }

    public static bool IsWrite => State.Value?.IsWrite == true;

    public static int IncrementSendCount()
    {
        var call = State.Value;
        if (call is null)
        {
            return 1;
        }

        return Interlocked.Increment(ref call.SendCount);
    }

    public static HttpStatusCode? LastStatus
    {
        get => State.Value?.LastStatus;
        set
        {
            if (State.Value is { } call)
            {
                call.LastStatus = value;
            }
        }
    }

    private sealed class CallState
    {
        public bool IsWrite;
        public int SendCount;
        public HttpStatusCode? LastStatus;
    }

    private sealed class Resetter : IDisposable
    {
        public void Dispose() => State.Value = null;
    }
}

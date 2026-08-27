using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<MaxioCallState?> CurrentState = new();
    private readonly MaxioCallState? _previous;

    private MaxioCallScope(bool guardWrite)
    {
        _previous = CurrentState.Value;
        State = new MaxioCallState(guardWrite);
        CurrentState.Value = State;
    }

    internal MaxioCallState State { get; }
    internal static MaxioCallState? Current => CurrentState.Value;

    internal static MaxioCallScope Begin(bool guardWrite) => new(guardWrite);

    public void Dispose()
    {
        CurrentState.Value = _previous;
    }
}

internal sealed class MaxioCallState
{
    private int _writeSendCount;

    internal MaxioCallState(bool guardWrite)
    {
        GuardWrite = guardWrite;
    }

    internal bool GuardWrite { get; }
    internal HttpStatusCode? LastStatusCode { get; set; }
    internal int IncrementWriteSendCount() => Interlocked.Increment(ref _writeSendCount);
}

internal sealed class MaxioDuplicateSendPreventedException : Exception
{
    internal MaxioDuplicateSendPreventedException()
        : base("A retry of a non-idempotent Maxio write was prevented.")
    {
    }
}

public sealed class MaxioHttpSafetyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = MaxioCallScope.Current;
        if (state is { GuardWrite: true } && request.Method == HttpMethod.Post && state.IncrementWriteSendCount() > 1)
        {
            throw new MaxioDuplicateSendPreventedException();
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (state is not null)
        {
            state.LastStatusCode = response.StatusCode;
        }

        return response;
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<MaxioCallState?> CurrentState = new();
    private readonly MaxioCallState? _previous;

    private MaxioCallScope(bool enforceSingleSend)
    {
        _previous = CurrentState.Value;
        State = new MaxioCallState(enforceSingleSend);
        CurrentState.Value = State;
    }

    public MaxioCallState State { get; }
    public static MaxioCallState? Current => CurrentState.Value;

    public static MaxioCallScope Begin(bool enforceSingleSend) => new(enforceSingleSend);

    public void Dispose() => CurrentState.Value = _previous;
}

internal sealed class MaxioCallState
{
    public MaxioCallState(bool enforceSingleSend) => EnforceSingleSend = enforceSingleSend;

    public bool EnforceSingleSend { get; }
    public int NetworkSendAttempts;
    public HttpStatusCode? LastStatusCode;
}

internal sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A repeated upstream subscription-create send was blocked.") { }
}

internal sealed class SingleSendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = MaxioCallScope.Current;
        if (state?.EnforceSingleSend == true &&
            Interlocked.Increment(ref state.NetworkSendAttempts) > 1)
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var state = MaxioCallScope.Current;
        if (state is not null)
        {
            state.LastStatusCode = response.StatusCode;
        }

        return response;
    }
}

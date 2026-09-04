using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Throws instead of letting the SDK's transport-retry pipeline re-send a payment write
/// it did not authorise. Provider writes (authorize/capture/refund) run inside a
/// <see cref="SingleSendScope"/>; a second attempt on the same logical call is blocked
/// before it reaches the network, and the caller treats the outcome as unknown.
/// </summary>
public sealed class SingleSendGuardHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = SingleSendScope.Current;
        var requiresSingleSend = state is not null && IsPaymentWrite(request);

        if (requiresSingleSend)
        {
            // Count per request (method + path): one logical write may legitimately send
            // several distinct requests (e.g. create order, then authorize), but never the
            // same one twice.
            if (!state!.TryClaimSend($"{request.Method} {request.RequestUri?.AbsolutePath}"))
            {
                // Never throw HttpRequestException here — that is the type the retry pipeline
                // handles, so refusing would itself be retried.
                throw new PaymentResendBlockedException();
            }
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (requiresSingleSend && IsTransportFailure(ex))
        {
            // The request may have reached the provider before the connection dropped; the
            // retry that would have followed is never authorised.
            state!.MarkUnsettled();
            throw;
        }
    }

    /// <summary>OAuth token traffic is excluded: it is safe to re-send and must stay refreshable.</summary>
    private static bool IsPaymentWrite(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path.Contains("/oauth2/token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete || request.Method == HttpMethod.Put;
    }

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;
}

/// <summary>
/// Ambient "this logical payment write must reach the provider at most once" scope.
/// Retries run inside the caller's async context, so the state flows into the handler.
/// </summary>
public sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<SingleSendScope?> _current = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _sendCounts = new();
    private volatile bool _transportFailed;

    public static SingleSendScope? Current => _current.Value;

    public static SingleSendScope Begin()
    {
        var scope = new SingleSendScope();
        _current.Value = scope;
        return scope;
    }

    public bool TryClaimSend(string sendKey)
    {
        // Retry attempts of the same request are sequential, so AddOrUpdate is race-free here.
        return _sendCounts.AddOrUpdate(sendKey, 1, (_, existing) => existing + 1) == 1;
    }

    public void MarkUnsettled() => _transportFailed = true;

    /// <summary>True once any request of this logical write has left the process.</summary>
    public bool AnySendAttempted => !_sendCounts.IsEmpty;

    /// <summary>A send left the process and the connection failed: the provider outcome is unknown.</summary>
    public bool OutcomeIsUnknown => _transportFailed;

    public void Dispose() => _current.Value = null;
}

/// <summary>Sentinel thrown by the guard; deliberately not an HttpRequestException (never retried).</summary>
public sealed class PaymentResendBlockedException : Exception
{
    public PaymentResendBlockedException()
        : base("The payment provider request was not re-sent: the outcome of the previous attempt is unknown.")
    {
    }
}

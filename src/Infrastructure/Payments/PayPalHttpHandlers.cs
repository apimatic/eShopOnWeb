using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalWriteScope
{
    private static readonly AsyncLocal<SendState?> State = new();

    public static IDisposable Begin()
    {
        var previous = State.Value;
        State.Value = new SendState();
        return new Popper(previous);
    }

    public static void CountSend()
    {
        var state = State.Value;
        if (state is null)
        {
            return;
        }

        if (state.Sent)
        {
            throw new PayPalDuplicateSendException();
        }

        state.Sent = true;
    }

    private sealed class SendState
    {
        public bool Sent { get; set; }
    }

    private sealed class Popper : IDisposable
    {
        private readonly SendState? _previous;
        public Popper(SendState? previous) => _previous = previous;
        public void Dispose() => State.Value = _previous;
    }
}

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException()
        : base("A duplicate PayPal write was blocked after an uncertain first attempt.")
    {
    }
}

internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isTokenRequest = path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
        if (!isTokenRequest &&
            request.Method != HttpMethod.Get &&
            request.Method != HttpMethod.Head &&
            request.Method != HttpMethod.Options)
        {
            PayPalWriteScope.CountSend();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal static class PayPalLastStatus
{
    private static readonly AsyncLocal<int?> Status = new();
    public static int? Current => Status.Value;
    public static void Set(int status) => Status.Value = status;
}

internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalLastStatus.Set((int)response.StatusCode);
        return response;
    }
}

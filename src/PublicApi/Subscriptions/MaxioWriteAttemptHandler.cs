using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioWriteAttemptHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && MaxioWriteAttemptScope.Current is { } scope && !scope.TryClaim())
        {
            throw new MaxioWriteAttemptBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class MaxioWriteAttemptBlockedException : Exception
{
}

internal sealed class MaxioWriteAttemptScope : IDisposable
{
    private static readonly AsyncLocal<MaxioWriteAttemptScope?> CurrentScope = new();
    private readonly MaxioWriteAttemptScope? _previous;
    private int _claimed;

    private MaxioWriteAttemptScope()
    {
        _previous = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    public static MaxioWriteAttemptScope? Current => CurrentScope.Value;

    public static MaxioWriteAttemptScope Begin() => new();

    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;

    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = _previous;
        }
    }
}

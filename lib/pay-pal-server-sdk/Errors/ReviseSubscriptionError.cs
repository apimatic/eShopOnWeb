using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ReviseSubscriptionError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private ReviseSubscriptionError(Optional<SubscriptionError> subscriptionErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static ReviseSubscriptionError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static ReviseSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<ReviseSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 422 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReviseSubscriptionErrorResponse : IErrorResponse<ReviseSubscriptionError>
{
    public static ReviseSubscriptionErrorResponse Instance { get; } = new();

    private ReviseSubscriptionErrorResponse()
    {
    }

    public Task<ReviseSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReviseSubscriptionError.Create(response, ct);
}

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ListSubscriptionsError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private ListSubscriptionsError(Optional<SubscriptionError> subscriptionErrorValue, Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static ListSubscriptionsError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static ListSubscriptionsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<ListSubscriptionsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListSubscriptionsErrorResponse : IErrorResponse<ListSubscriptionsError>
{
    public static ListSubscriptionsErrorResponse Instance { get; } = new();

    private ListSubscriptionsErrorResponse()
    {
    }

    public Task<ListSubscriptionsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListSubscriptionsError.Create(response, ct);
}

using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ListSubscriptionTransactionsError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private ListSubscriptionTransactionsError(Optional<SubscriptionError> subscriptionErrorValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static ListSubscriptionTransactionsError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static ListSubscriptionTransactionsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) => _subscriptionErrorValue.TryGetValue(out value);

    private static Task<ListSubscriptionTransactionsError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 500 => response.Json<SubscriptionError>().As(AsSubscriptionError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<ListSubscriptionTransactionsError> Response { get; } = new(Create);
}

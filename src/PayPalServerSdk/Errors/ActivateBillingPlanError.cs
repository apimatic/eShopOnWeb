using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ActivateBillingPlanError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private ActivateBillingPlanError(Optional<SubscriptionError> subscriptionErrorValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static ActivateBillingPlanError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static ActivateBillingPlanError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) => _subscriptionErrorValue.TryGetValue(out value);

    private static Task<ActivateBillingPlanError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            401 or 403 or 404 or 422 or 500 => response.Json<SubscriptionError>().As(AsSubscriptionError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<ActivateBillingPlanError> Response { get; } = new(Create);
}

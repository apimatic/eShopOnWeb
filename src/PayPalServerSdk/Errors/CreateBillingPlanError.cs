using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreateBillingPlanError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private CreateBillingPlanError(Optional<SubscriptionError> subscriptionErrorValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static CreateBillingPlanError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static CreateBillingPlanError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) => _subscriptionErrorValue.TryGetValue(out value);

    private static Task<CreateBillingPlanError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 401 or 403 or 422 or 500 => response.Json<SubscriptionError>().As(AsSubscriptionError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<CreateBillingPlanError> Response { get; } = new(Create);
}

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class UpdateBillingPlanPricingSchemesError : ApiError
{
    private readonly Optional<SubscriptionError> _subscriptionErrorValue;

    private UpdateBillingPlanPricingSchemesError(Optional<SubscriptionError> subscriptionErrorValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionErrorValue = subscriptionErrorValue;
    }

    private static UpdateBillingPlanPricingSchemesError AsSubscriptionError(SubscriptionError value) =>
        new(Optional<SubscriptionError>.Some(value), default);

    private static UpdateBillingPlanPricingSchemesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionError(out SubscriptionError value) =>
        _subscriptionErrorValue.TryGetValue(out value);

    internal static Task<UpdateBillingPlanPricingSchemesError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 422 or 500 => FromJson<SubscriptionError>(response, ct).As(AsSubscriptionError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateBillingPlanPricingSchemesErrorResponse : IErrorResponse<UpdateBillingPlanPricingSchemesError>
{
    public static UpdateBillingPlanPricingSchemesErrorResponse Instance { get; } = new();

    private UpdateBillingPlanPricingSchemesErrorResponse()
    {
    }

    public Task<UpdateBillingPlanPricingSchemesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateBillingPlanPricingSchemesError.Create(response, ct);
}

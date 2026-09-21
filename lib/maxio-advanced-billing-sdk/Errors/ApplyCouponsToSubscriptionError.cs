using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ApplyCouponsToSubscriptionError : ApiError
{
    private readonly Optional<SubscriptionAddCouponError1> _subscriptionAddCouponError1Value;

    private ApplyCouponsToSubscriptionError(Optional<SubscriptionAddCouponError1> subscriptionAddCouponError1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _subscriptionAddCouponError1Value = subscriptionAddCouponError1Value;
    }

    private static ApplyCouponsToSubscriptionError AsSubscriptionAddCouponError1(SubscriptionAddCouponError1 value) =>
        new(Optional<SubscriptionAddCouponError1>.Some(value), default);

    private static ApplyCouponsToSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSubscriptionAddCouponError1(out SubscriptionAddCouponError1 value) =>
        _subscriptionAddCouponError1Value.TryGetValue(out value);

    internal static Task<ApplyCouponsToSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<SubscriptionAddCouponError1>(response, ct).As(AsSubscriptionAddCouponError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ApplyCouponsToSubscriptionErrorResponse : IErrorResponse<ApplyCouponsToSubscriptionError>
{
    public static ApplyCouponsToSubscriptionErrorResponse Instance { get; } = new();

    private ApplyCouponsToSubscriptionErrorResponse()
    {
    }

    public Task<ApplyCouponsToSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ApplyCouponsToSubscriptionError.Create(response, ct);
}

using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderLine(int CatalogItemId, int Quantity);

public enum PlaceOrderOutcome
{
    Created,
    Invalid
}

public sealed record PlaceOrderResult(PlaceOrderOutcome Outcome, Order? Order, string? Message)
{
    public static PlaceOrderResult Created(Order order) => new(PlaceOrderOutcome.Created, order, null);
    public static PlaceOrderResult Invalid(string message) => new(PlaceOrderOutcome.Invalid, null, message);
}

public enum PayOrderOutcome
{
    Paid,
    /// <summary>The order had already been paid; nothing was charged by this request.</summary>
    AlreadyPaid,
    Refused,
    ActionRequired,
    Pending,
    /// <summary>The provider rejected the card data or request; no money was taken.</summary>
    Rejected,
    /// <summary>The provider could not be used; no money was taken.</summary>
    ProviderUnavailable,
    /// <summary>The provider did not answer in time; the outcome is recorded as unknown and settled on the next request.</summary>
    ProviderTimeout,
    /// <summary>The provider answered but the outcome could not be established; settled on the next request.</summary>
    ProviderUnknown,
    NotFound,
    /// <summary>Another payment operation on this order is in progress.</summary>
    Busy,
    /// <summary>The order cannot be paid (e.g. its total is not chargeable in the configured currency).</summary>
    NotPayable
}

public sealed record PayOrderResult(PayOrderOutcome Outcome, Order? Order, PaymentAttempt? Attempt, string? Message);

public enum RefundOrderOutcome
{
    Received,
    NotFound,
    Busy,
    NotPaid,
    Invalid,
    ExceedsRefundable,
    Rejected,
    ProviderUnavailable,
    ProviderTimeout,
    ProviderUnknown,
    /// <summary>An earlier refund's outcome is still unknown; no new refund was sent.</summary>
    PreviousRefundUnsettled
}

public sealed record RefundOrderResult(RefundOrderOutcome Outcome, Order? Order, OrderRefund? Refund, string? Message);

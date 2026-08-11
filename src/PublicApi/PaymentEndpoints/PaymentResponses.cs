using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Response for POST /api/orders. <c>OrderId</c> is a top-level field so the flow can be driven end to end.</summary>
public record PlaceOrderResponse(int OrderId, PaymentStateDto Payment);

/// <summary>Response for the pay/fulfil/cancel actions: the current payment state.</summary>
public record PaymentActionResponse(PaymentStateDto Payment);

/// <summary>Response for POST /api/orders/{orderId}/refunds. <c>RefundId</c> is a top-level field.</summary>
public record RefundResponse(string RefundId, RefundDto Refund, PaymentStateDto Payment);

public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);

/// <summary>Response for POST /api/payment-methods. <c>PaymentMethodId</c> is a top-level field.</summary>
public record SaveCardResponse(int PaymentMethodId, SavedCardDto PaymentMethod);

public record ListCardsResponse(IReadOnlyList<SavedCardDto> PaymentMethods);

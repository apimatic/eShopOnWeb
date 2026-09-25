using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Request body for <c>POST /api/orders</c>.</summary>
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineInput> Items, ShipToAddressInput? ShipToAddress);

/// <summary>Request body for <c>POST /api/orders/{orderId}/pay</c> — either a one-off card OR a saved card id.</summary>
public sealed record PayOrderRequest(CardInput? Card, int? SavedPaymentMethodId);

/// <summary>Request body for <c>POST /api/orders/{orderId}/refunds</c>. Amount omitted = full refund.</summary>
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

/// <summary>Request body for <c>POST /api/payment-methods</c> — the card to save.</summary>
public sealed record SavePaymentMethodRequest(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    BillingAddressInput? BillingAddress);

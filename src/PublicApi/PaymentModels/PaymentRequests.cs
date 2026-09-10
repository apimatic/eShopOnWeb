using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A single requested order line.</summary>
public record OrderItemRequestDto(int CatalogItemId, int Quantity);

/// <summary>Body of <c>POST /api/orders</c>: catalog items and quantities, with an optional shipping address.</summary>
public record CreateOrderRequest(List<OrderItemRequestDto> Items, OrderAddressDto? ShippingAddress);

/// <summary>Body of <c>POST /api/orders/{id}/pay</c>: pay with a one-off <see cref="Card"/> or a saved <see cref="PaymentMethodId"/>.</summary>
public record PayOrderRequest(CardDto? Card, int? PaymentMethodId);

/// <summary>Body of <c>POST /api/orders/{id}/refunds</c>: optional partial <see cref="Amount"/> and a required idempotency key.</summary>
public record RefundRequest(decimal? Amount, string IdempotencyKey);

/// <summary>Body of <c>POST /api/payment-methods</c>: the card to save and an optional label.</summary>
public record SavePaymentMethodRequest(CardDto Card, string? Alias);

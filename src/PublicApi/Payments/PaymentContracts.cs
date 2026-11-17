using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
}

public class PayOrderRequest
{
    public string? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
}

public class PayOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? PaymentMethodId { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public class FulfilOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? TransferId { get; set; }
    public string? CaptureState { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeCents { get; set; }
    public long NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
}

public class CancelOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public bool Released { get; set; }
}

public class RefundOrderRequest
{
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    [JsonPropertyName("refundId")]
    public string RefundId { get; set; } = string.Empty;

    public long RefundAmountCents { get; set; }
    public long TotalRefundedCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public string? RefundState { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class MyOrderResponse
{
    [JsonPropertyName("orderId")]
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long AuthorizedAmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeCents { get; set; }
    public long NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? TransferId { get; set; }
    public string? CaptureState { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public class SavePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
}

public class PaymentMethodResponse
{
    [JsonPropertyName("paymentMethodId")]
    public string PaymentMethodId { get; set; } = string.Empty;

    public string Last4 { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
}

public class ReconciliationResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public List<ReconciliationLine> Lines { get; set; } = new();
    public int FinixRecordCount { get; set; }
    public int EshopRecordCount { get; set; }
    public int PagesWalked { get; set; }
    public bool Truncated { get; set; }
    public string? NextCursor { get; set; }
}

public class ReconciliationLine
{
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? FinixId { get; set; }
    public int? OrderId { get; set; }
    public long? AmountCents { get; set; }
    public string? State { get; set; }
    public bool Matched { get; set; }
    public string? Issue { get; set; }
}

using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    public ShippingAddressDto ShipToAddress { get; set; } = new ShippingAddressDto();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

/// <summary>Raw card details for a one-off payment. Never stored; sent to the provider only.</summary>
public class CardDetailsDto
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressDto BillingAddress { get; set; } = new BillingAddressDto();
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>A saved card of the caller to pay with, instead of raw card details.</summary>
    public int? PaymentMethodId { get; set; }
    public CardDetailsDto? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Refund amount; omit for a full refund of what is still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: the same key never produces a second refund.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new CardDetailsDto();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; }

    public DeletePaymentMethodRequest(int paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }
}

public class PaymentRefundDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public int PaymentId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<PaymentRefundDto> Refunds { get; set; } = new List<PaymentRefundDto>();
}

public class OrderItemViewDto
{
    public int CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PictureUri { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemViewDto> Items { get; set; } = new List<OrderItemViewDto>();
    public PaymentStateDto? Payment { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<MyOrderDto> Orders { get; set; } = new List<MyOrderDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? InvoiceId { get; set; }
    public string? ReferenceId { get; set; }
}

public class ShopPaymentDto
{
    public int OrderId { get; set; }
    public string PaymentKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PayPalId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public bool Matched { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public string? LastRefreshedDatetime { get; set; }
    public List<ReconciliationTransactionDto> PayPalTransactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ShopPaymentDto> ShopPayments { get; set; } = new List<ShopPaymentDto>();
    public List<ReconciliationTransactionDto> PayPalOnly { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ShopPaymentDto> ShopOnly { get; set; } = new List<ShopPaymentDto>();
}




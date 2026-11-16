using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShippingAddressDto? ShippingAddress { get; set; }
}

public sealed class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressDto? Address { get; set; }
}

public sealed class CardAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public sealed class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public sealed class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class MyOrdersRequest : BaseRequest
{
}

public sealed class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public sealed class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();
}

public sealed class ListPaymentMethodsRequest : BaseRequest
{
}

public sealed class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public sealed class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? FinixAuthorizationId { get; set; }
    public string? FinixAuthorizationState { get; set; }
    public string? FinixVoidState { get; set; }
    public string? FinixTransferId { get; set; }
    public string? FinixTransferState { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FinixFeeCents { get; set; }
    public long NetAmountCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public string? FailureMessage { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public ShippingAddressDto ShipToAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public sealed class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class RefundDto
{
    public int RefundId { get; set; }
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? FinixTransferState { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<FinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> EshopOrders { get; set; } = new();
    public List<ReconciliationDiscrepancyDto> Discrepancies { get; set; } = new();
}

public sealed class FinixTransactionDto
{
    public string Source { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
}

public sealed class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? FinixAuthorizationId { get; set; }
    public string? FinixTransferId { get; set; }
    public bool Matched { get; set; }
}

public sealed class ReconciliationDiscrepancyDto
{
    public string Kind { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? FinixTransactionId { get; set; }
    public int? OrderId { get; set; }
}

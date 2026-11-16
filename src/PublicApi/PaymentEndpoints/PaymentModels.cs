using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = PaymentStatuses.AwaitingPayment;
}

public class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequest
{
    public string? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string? PaymentMethodId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? TransferId { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeCents { get; set; }
    public long NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
    public List<OrderLineDto> Items { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class RefundOrderRequest
{
    public long AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public long TotalRefundedCents { get; set; }
    public string State { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class SavePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
}

public class PaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string MatchStatus { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? TransferId { get; set; }
    public string? TransferType { get; set; }
    public string? TransferState { get; set; }
    public long? AmountCents { get; set; }
    public long? FeeCents { get; set; }
    public DateTimeOffset? TransferCreatedAt { get; set; }
    public string? Note { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public string? NextCursor { get; set; }
    public DateTimeOffset? CoveredTo { get; set; }
    public int ProviderTransactionCount { get; set; }
    public int AppOrderCount { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ApiErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? LogRef { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
}

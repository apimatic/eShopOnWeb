using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new Address(
        Street ?? string.Empty, City ?? string.Empty, State ?? string.Empty,
        Country ?? string.Empty, ZipCode ?? string.Empty);
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundDto> Refunds { get; set; } = new List<RefundDto>();

    public static PaymentDto FromPayment(Payment payment) => new PaymentDto
    {
        PaymentId = payment.Id,
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AuthorizedAmount = payment.AuthorizedAmount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RemainingRefundable = payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            ? payment.RefundableAmount
            : 0m,
        SavedPaymentMethodId = payment.SavedPaymentMethodId,
        Refunds = MapRefunds(payment)
    };

    private static List<RefundDto> MapRefunds(Payment payment)
    {
        var refunds = new List<RefundDto>();
        foreach (var refund in payment.Refunds)
        {
            refunds.Add(new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status,
                CreatedAt = refund.CreatedAt
            });
        }
        return refunds;
    }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "AwaitingPayment";
    public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    public PaymentDto? Payment { get; set; }
}

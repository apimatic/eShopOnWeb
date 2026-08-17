using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe, shopper-facing view of an order's payment state.</summary>
public class PaymentStateDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string? Instrument { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto? From(Payment? payment)
    {
        if (payment is null) return null;
        var dto = new PaymentStateDto
        {
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.CurrencyCode,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            CaptureId = payment.CaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            RefundableRemaining = payment.IsCaptured ? payment.RefundableRemaining : 0m,
            Instrument = payment.InstrumentDescription
        };
        foreach (var r in payment.Refunds)
        {
            dto.Refunds.Add(new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                IdempotencyKey = r.IdempotencyKey
            });
        }
        return dto;
    }
}

public class RefundDto
{
    public string? RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderLineDto
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

/// <summary>An order line summary in an order view.</summary>
public class OrderItemSummaryDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order paired with its payment state, for the shopper's own view.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string OrderDate { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemSummaryDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static OrderSummaryDto From(Order order, Payment? payment)
    {
        var dto = new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate.ToString("o"),
            Total = order.Total(),
            Payment = PaymentStateDto.From(payment)
        };
        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderItemSummaryDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }
        return dto;
    }
}

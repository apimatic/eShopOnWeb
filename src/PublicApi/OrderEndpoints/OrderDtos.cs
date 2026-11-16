using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long? AuthorizedAmountCents { get; set; }
    public decimal? AuthorizedAmount => AuthorizedAmountCents.HasValue ? AuthorizedAmountCents.Value / 100m : null;
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount => CapturedAmountCents.HasValue ? CapturedAmountCents.Value / 100m : null;
    public long? FinixFeeCents { get; set; }
    public decimal? FinixFee => FinixFeeCents.HasValue ? FinixFeeCents.Value / 100m : null;
    public long? NetProceedsCents { get; set; }
    public decimal? NetProceeds => NetProceedsCents.HasValue ? NetProceedsCents.Value / 100m : null;
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount => RefundedAmountCents / 100m;
    public string? PaymentInstrumentId { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureTransferState { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public AddressDto ShipToAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
}

public static class OrderResponseMapper
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            TotalCents = OrderPaymentService.ToCents(order.Total()),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            BuyerId = order.BuyerId,
            ShipToAddress = new AddressDto
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(item => new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                PictureUri = item.ItemOrdered.PictureUri,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
                LineTotal = item.UnitPrice * item.Units
            }).ToList(),
            Payment = ToPaymentDto(order)
        };
    }

    public static PaymentDto ToPaymentDto(Order order)
    {
        return new PaymentDto
        {
            Status = order.Status.ToString(),
            Currency = order.Currency,
            AuthorizedAmountCents = order.AuthorizedAmountCents,
            CapturedAmountCents = order.CapturedAmountCents,
            FinixFeeCents = order.FinixFeeCents,
            NetProceedsCents = order.NetProceedsCents,
            RefundedAmountCents = order.RefundedAmountCents,
            PaymentInstrumentId = order.PaymentInstrumentId,
            PaymentMethodId = order.SavedPaymentMethodId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationState = order.AuthorizationState,
            CaptureTransferId = order.CaptureTransferId,
            CaptureTransferState = order.CaptureTransferState,
            FailureMessage = order.PaymentFailureMessage,
            Refunds = order.Refunds.Select(ToDto).ToList()
        };
    }

    public static RefundDto ToDto(PaymentRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.RefundId,
            AmountCents = refund.AmountCents,
            Amount = refund.AmountCents / 100m,
            Status = refund.Status,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }
}

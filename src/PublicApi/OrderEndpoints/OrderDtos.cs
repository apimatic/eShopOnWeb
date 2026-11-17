using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? VoidState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public long CapturedAmountCents { get; set; }
    public long FeeCents { get; set; }
    public long NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? Currency { get; set; }
    public string? InstrumentId { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? FailureMessage { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public long TotalAmountCents { get; set; }
    public string? Currency { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }
}

public class CardAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            TotalAmountCents = order.TotalAmountCents,
            Currency = order.PaymentCurrency,
            Payment = new PaymentStateDto
            {
                AuthorizationId = order.PaymentAuthorizationId,
                AuthorizationState = order.PaymentAuthorizationState,
                VoidState = order.PaymentVoidState,
                CaptureTransferId = order.PaymentCaptureTransferId,
                CaptureState = order.PaymentCaptureState,
                AuthorizedAmountCents = order.PaymentAmountCents,
                CapturedAmountCents = order.PaymentCapturedAmountCents,
                FeeCents = order.PaymentFeeCents,
                NetCents = order.PaymentNetCents,
                RefundedAmountCents = order.RefundedAmountCents,
                RefundableAmountCents = order.RefundableAmountCents,
                Currency = order.PaymentCurrency,
                InstrumentId = order.PaymentInstrumentId,
                PaymentMethodId = order.PaymentMethodId,
                FailureMessage = order.PaymentFailureMessage
            },
            Items = order.OrderItems
                .Select(item => new OrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                })
                .ToList(),
            Refunds = order.Refunds
                .Select(refund => new RefundDto
                {
                    RefundId = refund.FinixTransferId,
                    AmountCents = refund.AmountCents,
                    Status = refund.Status,
                    IdempotencyKey = refund.IdempotencyKey,
                    CreatedAt = refund.CreatedAt
                })
                .ToList()
        };

        return dto;
    }

    public static OrderActionResponse ToActionResponse(Order order, Guid correlationId)
    {
        var dto = ToDto(order);
        return new OrderActionResponse(correlationId)
        {
            OrderId = dto.OrderId,
            Status = dto.Status,
            TotalAmountCents = dto.TotalAmountCents,
            Currency = dto.Currency,
            Payment = dto.Payment,
            Refunds = dto.Refunds
        };
    }
}

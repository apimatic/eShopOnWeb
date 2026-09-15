using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps domain entities to the API response shapes. Never surfaces card numbers.</summary>
public static class PaymentMappings
{
    public static OrderPaymentResponse ToResponse(this Order order)
    {
        return new OrderPaymentResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? string.Empty,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment?.ToDto()
        };
    }

    public static PaymentDto ToDto(this OrderPayment payment)
    {
        var dto = new PaymentDto
        {
            Provider = payment.Provider,
            Status = payment.Status.ToString(),
            PaymentReference = payment.PaymentReference,
            PaymentMethod = payment.PaymentMethodDescription,
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };

        if (payment.AuthorizationId is not null)
        {
            dto.Authorization = new AuthorizationDto
            {
                Id = payment.AuthorizationId,
                Status = payment.AuthorizationStatus,
                ExpiresAt = payment.AuthorizationExpiresAt
            };
        }

        if (payment.CaptureId is not null)
        {
            dto.Capture = new CaptureDto
            {
                Id = payment.CaptureId,
                Status = payment.CaptureStatus,
                Amount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                CapturedAt = payment.CapturedAt
            };
        }

        return dto;
    }

    public static PaymentMethodResponse ToResponse(this SavedCard card)
    {
        return new PaymentMethodResponse
        {
            PaymentMethodId = card.Id,
            Provider = card.Provider,
            Brand = card.Brand,
            Last4 = card.Last4,
            Expiry = card.ExpiryYearMonth,
            Label = card.DisplayLabel,
            CardholderName = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }

    public static CardDetails ToCardDetails(this CardDto card)
    {
        return new CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.CardholderName,
            new CardBillingAddress(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode));
    }
}

using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps domain entities to safe API DTOs and request fragments to gateway inputs.</summary>
public static class PaymentMappings
{
    public static OrderDto ToDto(this Order order)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (order.Payment is Payment payment)
        {
            dto.Payment = new PaymentDto
            {
                Amount = payment.Amount,
                Currency = payment.CurrencyCode,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
                InstrumentDescription = payment.InstrumentDescription,
                CaptureId = payment.CaptureId,
                CaptureStatus = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                TotalRefunded = payment.TotalRefunded(),
                RefundableRemaining = payment.RefundableRemaining(),
                Refunds = payment.Refunds.Select(r => new RefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }

        return dto;
    }

    public static PaymentMethodDto ToDto(this CustomerPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        Last4 = method.Last4,
        ExpiryMonth = method.ExpiryMonth,
        ExpiryYear = method.ExpiryYear,
        Alias = method.Alias,
        DisplayName = method.DisplayName,
        CreatedAt = method.CreatedAt
    };

    public static CardDetails ToCardDetails(this CardDto card) => new(
        Number: card.Number,
        ExpiryMonth: card.ExpiryMonth,
        ExpiryYear: card.ExpiryYear,
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress is null
            ? null
            : new CardBillingAddress(
                AddressLine1: card.BillingAddress.Street,
                AddressLine2: null,
                AdminArea1: card.BillingAddress.State,
                AdminArea2: card.BillingAddress.City,
                PostalCode: card.BillingAddress.ZipCode,
                CountryCode: card.BillingAddress.Country));

    public static Address ToDomain(this AddressDto? dto) => dto is null
        ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
        : new Address(
            dto.Street ?? "N/A",
            dto.City ?? "N/A",
            dto.State ?? "N/A",
            dto.Country ?? "N/A",
            dto.ZipCode ?? "N/A");
}

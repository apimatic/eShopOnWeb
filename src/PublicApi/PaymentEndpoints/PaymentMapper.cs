using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps domain entities to safe response DTOs and request DTOs to domain inputs.</summary>
public static class PaymentMapper
{
    public static string? BuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static PaymentStateDto ToDto(this Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                IdempotencyKey = r.IdempotencyKey,
                Amount = r.Amount,
                Currency = r.Currency,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            })
            .ToList()
    };

    public static OrderSummaryDto ToSummary(this OrderWithPayment op)
    {
        var order = op.Order;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = op.Payment?.Currency ?? string.Empty,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = op.Payment?.ToDto()
        };
    }

    public static SavedCardDto ToDto(this SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };

    public static CardDetails ToCardDetails(this CardDto card) => new(
        Number: (card.Number ?? string.Empty).Replace(" ", string.Empty).Trim(),
        Expiry: (card.Expiry ?? string.Empty).Trim(),
        SecurityCode: (card.SecurityCode ?? string.Empty).Trim(),
        CardholderName: (card.CardholderName ?? string.Empty).Trim(),
        BillingAddress: card.BillingAddress is null ? null : new BillingAddress(
            AddressLine1: card.BillingAddress.AddressLine1,
            AddressLine2: card.BillingAddress.AddressLine2,
            AdminArea2: card.BillingAddress.City,
            AdminArea1: card.BillingAddress.State,
            PostalCode: card.BillingAddress.PostalCode,
            CountryCode: card.BillingAddress.CountryCode));
}

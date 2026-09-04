using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

public static class PaymentApiExtensions
{
    public static string RequireBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new UnauthorizedAccessException("A valid JWT bearer token is required.");
        }
        return buyerId;
    }

    public static PayPalCardPayment? ToCardPayment(this PayPalCardRequest? card)
    {
        if (card == null) return null;
        return new PayPalCardPayment(
            card.Number?.Trim() ?? "",
            card.Expiry?.Trim() ?? "",
            card.Name?.Trim() ?? "",
            new PayPalCardAddress(
                card.BillingAddress?.Line1 ?? "",
                card.BillingAddress?.Line2 ?? "",
                card.BillingAddress?.City ?? "",
                card.BillingAddress?.State ?? "",
                card.BillingAddress?.PostalCode ?? "",
                card.BillingAddress?.CountryCode ?? ""));
    }

    public static OrderPaymentDto ToDto(this Payment payment)
    {
        var dto = new OrderPaymentDto
        {
            PaymentId = payment.Id,
            Currency = payment.Currency,
            AmountAuthorized = payment.AmountAuthorized,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            SavedPaymentMethodId = payment.SavedPaymentMethodId,
            AuthorizedAt = payment.AuthorizedAt,
            CapturedAt = payment.CapturedAt
        };
        foreach (var refund in payment.Refunds)
        {
            dto.Refunds.Add(new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status,
                IdempotencyKey = refund.IdempotencyKey,
                CreatedAt = refund.CreatedAt
            });
        }
        return dto;
    }

    public static OrderItemDto ToDto(this OrderItem item)
    {
        return new OrderItemDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Units = item.Units
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

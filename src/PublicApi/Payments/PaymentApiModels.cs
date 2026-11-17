using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AddressRequest
{
    public string? Line1 { get; set; }
    public string? Street { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }

    public FinixAddress? ToFinixAddress()
    {
        if (string.IsNullOrWhiteSpace(Line1 ?? Street) &&
            string.IsNullOrWhiteSpace(City) &&
            string.IsNullOrWhiteSpace(PostalCode ?? ZipCode))
        {
            return null;
        }

        return new FinixAddress(
            Line1 ?? Street ?? "123 Main St",
            Line2,
            City ?? "San Francisco",
            Region ?? State ?? "CA",
            PostalCode ?? ZipCode ?? "94105",
            Country ?? "USA");
    }

    public Address ToOrderAddress() =>
        new(
            Line1 ?? Street ?? "123 Main St",
            City ?? "San Francisco",
            Region ?? State ?? "CA",
            Country ?? "USA",
            PostalCode ?? ZipCode ?? "94105");
}

public class CardDetailsRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }
    public AddressRequest? Address { get; set; }

    public CardPaymentInput ToCardPaymentInput() =>
        new(
            Number ?? CardNumber ?? string.Empty,
            ExpirationMonth ?? ExpMonth ?? 0,
            ExpirationYear ?? ExpYear ?? 0,
            SecurityCode ?? Cvc ?? Cvv,
            Name ?? CardholderName ?? "eShop Shopper",
            (BillingAddress ?? Address)?.ToFinixAddress());
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int? Id { get; set; }
    public int? ProductId { get; set; }
    public int Quantity { get; set; } = 1;

    public int ResolveCatalogItemId() => CatalogItemId != 0 ? CatalogItemId : Id ?? ProductId ?? 0;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string FinixReversalId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int AuthorizedAmountCents { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public int CapturedAmountCents { get; set; }
    public decimal CapturedAmount { get; set; }
    public int FeeCents { get; set; }
    public decimal Fee { get; set; }
    public int NetCents { get; set; }
    public decimal Net { get; set; }
    public int RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? FinixAuthorizationId { get; set; }
    public string? FinixAuthorizationState { get; set; }
    public DateTimeOffset? FinixAuthorizationExpiresAt { get; set; }
    public string? FinixCaptureTransferId { get; set; }
    public string? FinixCaptureState { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = "NotStarted";
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class SavedCardResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Name { get; set; } = string.Empty;
}

public static class PaymentApiMapping
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value
                      ?? user.Identity?.Name
                      ?? user.FindFirst("name")?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The access token does not identify a shopper.");
        }

        return buyerId;
    }

    public static OrderResponse ToOrderResponse(this OrderDetails details, string fallbackCurrency)
    {
        var order = details.Order;
        var payment = details.Payment;

        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.Currency ?? fallbackCurrency,
            PaymentStatus = payment?.Status.ToString() ?? "NotStarted",
            Items = order.OrderItems.Select(item => new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Units,
                LineTotal = item.UnitPrice * item.Units
            }).ToList(),
            Payment = payment == null ? null : new PaymentDto
            {
                PaymentId = payment.Id,
                Status = payment.Status.ToString(),
                Currency = payment.Currency,
                AuthorizedAmountCents = payment.AuthorizedAmountCents,
                AuthorizedAmount = FromCents(payment.AuthorizedAmountCents),
                CapturedAmountCents = payment.CapturedAmountCents,
                CapturedAmount = FromCents(payment.CapturedAmountCents),
                FeeCents = payment.FeeCents,
                Fee = FromCents(payment.FeeCents),
                NetCents = payment.NetCents,
                Net = FromCents(payment.NetCents),
                RefundedAmountCents = payment.RefundedAmountCents,
                RefundedAmount = FromCents(payment.RefundedAmountCents),
                FinixAuthorizationId = payment.FinixAuthorizationId,
                FinixAuthorizationState = payment.FinixAuthorizationState,
                FinixAuthorizationExpiresAt = payment.FinixAuthorizationExpiresAt,
                FinixCaptureTransferId = payment.FinixCaptureTransferId,
                FinixCaptureState = payment.FinixCaptureState,
                FailureCode = payment.FailureCode,
                FailureMessage = payment.FailureMessage,
                Refunds = payment.Refunds.Select(ToRefundDto).ToList()
            }
        };
    }

    public static RefundDto ToRefundDto(this PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        FinixReversalId = refund.FinixReversalId,
        State = refund.FinixState,
        AmountCents = refund.AmountCents,
        Amount = FromCents(refund.AmountCents),
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };

    public static SavedCardResponse ToSavedCardResponse(this SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastFour = card.LastFour,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear,
        Name = card.Name
    };

    public static int? ToCents(decimal? amount) =>
        amount.HasValue ? (int)Math.Round(amount.Value * 100m, MidpointRounding.AwayFromZero) : null;

    private static decimal FromCents(int cents) => cents / 100m;
}

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Shared card input (never stored or logged by this app) -----

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM form (e.g. "2030-01").</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

// ----- Requests -----

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequestDto
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequestDto
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead of <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequestDto
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ----- Responses -----

public class RefundStateDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
}

public class PaymentStateDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? AuthorizationStatus { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<RefundStateDto> Refunds { get; set; } = new();
}

public class CreateOrderResponseDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayOrderResponseDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

public class FulfilOrderResponseDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class CancelOrderResponseDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RefundOrderResponseDto
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
}

public class MyOrdersResponseDto
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = string.Empty;
    public decimal? PayPalAmount { get; set; }
    public decimal EshopAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public string EshopStatus { get; set; } = string.Empty;
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
}

public class ReconciliationEshopEntryDto
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ReconciliationResponseDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EshopCapturedCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationTransactionDto> InPayPalOnly { get; set; } = new();
    public List<ReconciliationEshopEntryDto> InEshopOnly { get; set; } = new();
}

// ----- Mapping helpers -----

internal static class PaymentMapping
{
    public static string GetBuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The caller identity could not be determined.");

    public static PayPalCardData ToCardData(CardDto card) => new(
        Number: card.Number,
        Expiry: card.Expiry,
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress is null
            ? null
            : new PayPalBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
}

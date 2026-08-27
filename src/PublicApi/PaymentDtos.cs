using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity from the JWT (ClaimTypes.Name).</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Raw card details for a one-off payment or for saving a card. Never stored, never logged.</summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string? LastFailureReason { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromPayment(Payment payment) => new()
    {
        PaymentId = payment.Id,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RemainingRefundable = payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            ? payment.RefundableAmount()
            : 0m,
        LastFailureReason = payment.LastFailureReason,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Currency = r.Currency,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

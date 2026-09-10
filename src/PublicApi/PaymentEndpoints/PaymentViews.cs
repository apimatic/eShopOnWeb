using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A single refund as returned to callers.</summary>
public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

/// <summary>
/// The payment state of an order as returned to callers — everything PayPal owns (ids and statuses)
/// plus the captured/fee/net figures and refund history. Never carries card details.
/// </summary>
public record PaymentView(
    int OrderId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    decimal RefundableAmount,
    IReadOnlyList<RefundView> Refunds)
{
    public static PaymentView From(Payment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.Amount,
        p.CurrencyCode,
        p.InvoiceId,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.RefundedAmount,
        p.RefundableAmount,
        p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.RefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList());
}

/// <summary>Reads the caller's identity (email/username) from the validated JWT.</summary>
public static class CallerIdentity
{
    public static string? GetBuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}

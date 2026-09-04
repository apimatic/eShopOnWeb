using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentDtos;

/// <summary>
/// Read models for payment state. Built from the order aggregate only — they can carry
/// provider ids and statuses, never anything card-shaped.
/// </summary>
public class PaymentDto
{
    public string Provider { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string AuthorizationStatus { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public bool AuthorizationExpired { get; set; }
    public bool AwaitingRecovery { get; set; }
    public CaptureDto? Capture { get; set; }
    public List<RefundEntryDto> Refunds { get; set; } = new();
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }

    public static PaymentDto? From(Order order)
    {
        var payment = order.Payment;
        if (payment is null)
        {
            return null;
        }

        return new PaymentDto
        {
            Provider = payment.ProviderName,
            ProviderOrderId = NullIfEmpty(payment.ProviderOrderId),
            AuthorizationId = NullIfEmpty(payment.AuthorizationId),
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizedAmount = payment.AuthorizedAmount,
            Currency = payment.CurrencyCode,
            AuthorizationExpiresAt = payment.AuthorizationExpirationTime,
            AuthorizationExpired = payment.AuthorizationExpired,
            AwaitingRecovery = payment.HasPendingAuthorizationToRecover,
            Capture = string.IsNullOrEmpty(payment.CaptureId)
                ? null
                : new CaptureDto
                {
                    CaptureId = payment.CaptureId,
                    Status = payment.CaptureStatus,
                    AmountCaptured = payment.CapturedAmount ?? 0m,
                    FeeAmount = payment.FeeAmount,
                    NetAmount = payment.NetAmount,
                    Currency = payment.CurrencyCode,
                    CapturedAt = payment.CapturedAt
                },
            Refunds = order.Refunds.Select(r => new RefundEntryDto
            {
                RefundId = r.ProviderRefundId,
                Amount = r.Amount,
                Currency = r.CurrencyCode,
                Status = r.Status,
                TotalRefundedAmount = r.TotalRefundedAmount,
                RefundedAt = r.RefundedAt,
                IdempotencyKey = r.IdempotencyKey
            }).ToList(),
            RefundedAmount = order.RefundedAmount(),
            RemainingRefundableAmount = order.RemainingRefundableAmount()
        };
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

public class CaptureDto
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal AmountCaptured { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; set; }
}

public class RefundEntryDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? TotalRefundedAmount { get; set; }
    public DateTimeOffset RefundedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavedCardDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

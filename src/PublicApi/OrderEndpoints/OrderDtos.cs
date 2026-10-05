using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Payment state as the provider reported it. Never contains card details beyond brand and last digits.</summary>
public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public int? PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
    public string? PayPalOrderId { get; set; }
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public DateTimeOffset? VoidedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AuthorizationDto
{
    public string? Id { get; set; }
    public string? OriginalId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? ReauthorizedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalRefundId { get; set; }
    public string? PayPalStatus { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, Payment? payment, string currency) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = payment?.Currency ?? currency,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units,
        }).ToList(),
        Payment = payment is null ? null : ToDto(payment),
    };

    public static PaymentDto ToDto(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Provider = payment.Provider,
        Amount = payment.Amount,
        Currency = payment.Currency,
        InvoiceId = payment.InvoiceId,
        PaymentMethodId = payment.PaymentMethodId,
        CardBrand = payment.CardBrand,
        CardLastDigits = payment.CardLastDigits,
        PayPalOrderId = payment.ProviderOrderId,
        Authorization = payment.AuthorizationId is null ? null : new AuthorizationDto
        {
            Id = payment.AuthorizationId,
            OriginalId = payment.OriginalAuthorizationId,
            Status = payment.AuthorizationStatus,
            AuthorizedAt = payment.AuthorizedAt,
            ReauthorizedAt = payment.ReauthorizedAt,
            ExpiresAt = payment.AuthorizationExpiresAt,
        },
        Capture = payment.CaptureId is null ? null : new CaptureDto
        {
            Id = payment.CaptureId,
            Status = payment.CaptureStatus,
            Amount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            CapturedAt = payment.CapturedAt,
        },
        RefundedAmount = payment.RefundedAmount,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.OrderBy(r => r.Id).Select(ToDto).ToList(),
        VoidedAt = payment.VoidedAt,
        LastError = payment.LastError,
        UpdatedAt = payment.UpdatedAt,
    };

    public static RefundDto ToDto(PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        IdempotencyKey = refund.IdempotencyKey,
        Amount = refund.Amount,
        Status = refund.Status.ToString(),
        PayPalRefundId = refund.ProviderRefundId,
        PayPalStatus = refund.ProviderStatus,
        FailureReason = refund.FailureReason,
        RequestedAt = refund.RequestedAt,
        CompletedAt = refund.CompletedAt,
    };
}

/// <summary>Card details as posted by the caller. Never logged: <see cref="ToString"/> is redacted.</summary>
public class CardInput
{
    public string? Number { get; set; }
    /// <summary>YYYY-MM</summary>
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        CardValidator.NormaliseNumber(Number),
        Expiry?.Trim() ?? string.Empty,
        string.IsNullOrWhiteSpace(SecurityCode) ? null : SecurityCode.Trim(),
        string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        BillingAddress?.ToAddress());

    public override string ToString() => "[card details redacted]";
}

public class BillingAddressInput
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, (CountryCode ?? string.Empty).Trim().ToUpperInvariant());
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity as carried by the JWT (the same name the app uses as BuyerId).</summary>
    public static string? BuyerId(this ClaimsPrincipal user) =>
        string.IsNullOrWhiteSpace(user.Identity?.Name) ? null : user.Identity!.Name;
}

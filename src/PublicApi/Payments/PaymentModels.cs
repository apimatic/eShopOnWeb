using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CardInput
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class OrderItemInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class AddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed record OrderPaymentView(
    int OrderId,
    string Currency,
    PaymentStatus Status,
    string StatusName,
    long AmountCents,
    string? AuthorizationId,
    string? AuthorizationState,
    long? AuthorizedAmountCents,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureTransferId,
    string? CaptureState,
    long? CapturedAmountCents,
    long? FeeCents,
    long? NetCents,
    long RefundedAmountCents,
    string? SavedCardId,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset CreatedAt);

public sealed record RefundView(
    string RefundId,
    int OrderId,
    long AmountCents,
    string State,
    DateTimeOffset CreatedAt);

public sealed record SavedCardView(
    string PaymentMethodId,
    string? Brand,
    string? LastFour,
    int ExpirationMonth,
    int ExpirationYear,
    string? Name,
    bool Enabled);

public sealed record ReconciliationEntry(
    string Kind,
    string FinixId,
    string? OrderId,
    long? AmountCents,
    string? State,
    DateTimeOffset? CreatedAt,
    string MatchStatus,
    string? Detail);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    IReadOnlyList<ReconciliationEntry> FinixTransactions,
    IReadOnlyList<ReconciliationEntry> EshopOrders);

public static class Money
{
    /// <summary>Converts a decimal currency amount to the integer minor unit Finix expects.</summary>
    public static long ToCents(decimal amount)
    {
        var cents = amount * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new PaymentOperationException(
                400, $"Order total '{amount}' has sub-cent precision and cannot be charged to the cent.");
        }
        if (cents <= 0)
        {
            throw new PaymentOperationException(400, "Order total must be greater than zero.");
        }
        if (cents > long.MaxValue)
        {
            throw new PaymentOperationException(400, "Order total is too large.");
        }
        return (long)cents;
    }
}

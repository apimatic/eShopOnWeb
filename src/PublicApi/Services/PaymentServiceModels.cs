using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed record AddressDetails(
    string? Line1,
    string? City,
    string? Region,
    string? PostalCode,
    string? Country);

public sealed record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string? CardholderName,
    AddressDetails? Address)
{
    public string FirstName => SplitName().First;
    public string LastName => SplitName().Last;

    private (string First, string Last) SplitName()
    {
        var name = string.IsNullOrWhiteSpace(CardholderName) ? "eShop Shopper" : CardholderName.Trim();
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (parts[0], "Shopper"),
            _ => (parts[0], parts[^1])
        };
    }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<FinixTransactionRecord> FinixTransactions { get; set; } = [];
    public List<EshopTransactionRecord> EshopTransactions { get; set; } = [];
    public List<FinixTransactionRecord> UnmatchedFinixTransactions { get; set; } = [];
    public List<EshopTransactionRecord> UnmatchedEshopTransactions { get; set; } = [];
}

public sealed class FinixTransactionRecord
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransfer { get; set; }
    public string? TraceId { get; set; }
    public int? OrderId { get; set; }
    public string? EshopKind { get; set; }
    public bool Matched { get; set; }
}

public sealed class EshopTransactionRecord
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string? RefundId { get; set; }
    public bool Matched { get; set; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>
/// The shop's own copy of a transaction on a connected bank account (pending ones included).
/// </summary>
public class BankTransaction : BaseEntity
{
    public int ConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Plaid convention: positive amounts are money leaving the account.</summary>
    public decimal Amount { get; set; }
    public string? IsoCurrencyCode { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
    public string? PendingTransactionId { get; set; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

/// <summary>The shop's own copy of a transaction reported by Plaid (pending ones included).</summary>
public class BankTransaction : BaseEntity
{
    public int ConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public bool Pending { get; set; }
}

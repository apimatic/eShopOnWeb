using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>
/// The shop's own copy of a transaction on a connected bank account.
/// </summary>
public class BankTransaction : BaseEntity
{
    public int BankConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public bool Pending { get; set; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>The shop's copy of a transaction on a connected bank account, pending ones included.</summary>
public class BankTransaction : BaseEntity
{
    public int ConnectionId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public bool Pending { get; set; }
}

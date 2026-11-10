using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>The shop's own copy of a transaction on a connected bank account, pending ones included.</summary>
public class BankTransaction : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BankTransaction() { }
#pragma warning restore CS8618

    public BankTransaction(int connectionId, string plaidTransactionId)
    {
        ConnectionId = connectionId;
        PlaidTransactionId = plaidTransactionId;
    }

    public int ConnectionId { get; private set; }
    public string PlaidTransactionId { get; private set; }
    public string AccountId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTime Date { get; private set; }
    public bool Pending { get; private set; }
    public string? PendingTransactionId { get; private set; }

    public void Update(string accountId, string name, decimal amount, DateTime date, bool pending, string? pendingTransactionId)
    {
        AccountId = accountId;
        Name = name;
        Amount = amount;
        Date = date;
        Pending = pending;
        PendingTransactionId = pendingTransactionId;
    }
}

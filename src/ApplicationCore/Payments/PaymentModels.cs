using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardDetails
{
    public string Number { get; init; } = string.Empty;
    public int ExpirationMonth { get; init; }
    public int ExpirationYear { get; init; }
    public string? SecurityCode { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

public sealed class NewOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class OrderWithRefunds
{
    public OrderWithRefunds(Entities.OrderAggregate.Order order, IReadOnlyList<Entities.PaymentAggregate.OrderRefund> refunds)
    {
        Order = order;
        Refunds = refunds;
    }

    public Entities.OrderAggregate.Order Order { get; }
    public IReadOnlyList<Entities.PaymentAggregate.OrderRefund> Refunds { get; }
}

public sealed class ReconciliationTransaction
{
    public string Source { get; init; } = "Finix";
    public string Type { get; init; } = string.Empty;
    public string FinixId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; init; }
    public int? OrderId { get; init; }
    public bool Matched { get; init; }
    public string? Note { get; init; }
}

public sealed class ReconciliationReport
{
    public System.DateTimeOffset From { get; init; }
    public System.DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationTransaction> FinixTransactions { get; init; } = new List<ReconciliationTransaction>();
    public IReadOnlyList<ReconciliationTransaction> EshopOnlyTransactions { get; init; } = new List<ReconciliationTransaction>();
    public int FinixTransactionCount => FinixTransactions.Count;
    public int MatchedCount { get; init; }
    public int UnmatchedFinixCount { get; init; }
    public int EshopOnlyCount => EshopOnlyTransactions.Count;
}

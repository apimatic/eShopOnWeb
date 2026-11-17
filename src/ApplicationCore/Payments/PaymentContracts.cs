using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class CardBillingAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddress? Address { get; set; }
}

public class PayOrderCommand
{
    public int? PaymentMethodId { get; set; }
    public CardDetails? Card { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationTransaction> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> UnmatchedOrders { get; set; } = new();
}

public class ReconciliationTransaction
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public string? MatchKind { get; set; }
    public bool Matched => OrderId.HasValue;
}

public class ReconciliationOrder
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public string? Status { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public List<string> RefundTransferIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

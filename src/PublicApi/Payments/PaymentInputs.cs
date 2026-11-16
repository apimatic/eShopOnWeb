using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(
    string? Street,
    string? City,
    string? State,
    string? Country,
    string? ZipCode);

public sealed record CardAddressInput(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);

public sealed record CardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name,
    CardAddressInput? Address);

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PagesFetched { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationTransaction> Transactions { get; } = new();
    public List<UnmatchedEshopTransaction> UnmatchedEshop { get; } = new();
}

public sealed class ReconciliationTransaction
{
    public string TransferId { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public long? FeeCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchedRefundId { get; set; }
    public string? MatchKind { get; set; }
    public bool Matched => MatchKind is not null;
}

public sealed class UnmatchedEshopTransaction
{
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string? RefundId { get; set; }
    public string TransferId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
}

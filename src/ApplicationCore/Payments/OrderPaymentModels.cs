using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public sealed class ShippingAddressInput
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "NA";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "00000";
}

public sealed class PayOrderInput
{
    public int? PaymentMethodId { get; set; }
    public CardDetails? Card { get; set; }
}

public sealed class RefundInput
{
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }

    public long? ResolveAmountCents()
    {
        if (AmountCents.HasValue)
        {
            return AmountCents.Value;
        }

        if (Amount.HasValue)
        {
            return (long)Math.Round(Amount.Value * 100m, MidpointRounding.AwayFromZero);
        }

        return null;
    }
}

public sealed class ReconciliationFinixTransaction
{
    public string Id { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public int? MatchedOrderId { get; set; }
    public bool IsMatched { get; set; }
}

public sealed class ReconciliationOrder
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public List<string> RefundIds { get; set; } = new();
    public List<string> MatchedFinixTransactionIds { get; set; } = new();
    public bool IsMatched => MatchedFinixTransactionIds.Count > 0;
}

public sealed class ReconciliationResult
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationFinixTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> Orders { get; set; } = new();
    public List<ReconciliationFinixTransaction> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> UnmatchedOrders { get; set; } = new();
}

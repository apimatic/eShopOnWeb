using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = "";
    public string? PayPalReferenceId { get; set; }
    public string? PayPalReferenceIdType { get; set; }
    public string Status { get; set; } = "";
    public string EventCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string PayPalOrderId { get; set; } = "";
    public bool AmountMismatch { get; set; }
    public ReconciliationTransactionDto PayPalTransaction { get; set; } = null!;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>PayPal transactions that line up with an eShop order and payment record.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Transactions PayPal has on record that do not match any eShop order in range.</summary>
    public List<ReconciliationTransactionDto> PayPalOnly { get; set; } = new();

    /// <summary>eShop orders with a payment in range that PayPal's report did not return (e.g. reporting lag).</summary>
    public List<OrderDto> EShopOnly { get; set; } = new();
}

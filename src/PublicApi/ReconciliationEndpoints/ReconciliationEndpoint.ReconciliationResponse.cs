using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>An eShop order and the PayPal transaction it corresponds to.</summary>
public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal EShopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
}

/// <summary>A PayPal transaction with no corresponding eShop order — PayPal knows about it, eShop doesn't.</summary>
public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

/// <summary>An eShop order with payment activity but no corresponding PayPal transaction in this range — eShop knows about it, PayPal's report doesn't (yet).</summary>
public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? AuthorizationId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
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
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationTransactionDto> PayPalOnly { get; set; } = new();
    public List<ReconciliationOrderDto> EShopOnly { get; set; } = new();
}

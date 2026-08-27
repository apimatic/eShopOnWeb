using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? Time { get; set; }

    /// <summary>"matched" when lined up with an eShop order, "paypalOnly" otherwise.</summary>
    public string Match { get; set; } = string.Empty;
    public int? OrderId { get; set; }
}

public class ReconciliationLocalPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Always "eshopOnly": eShop knows this payment, PayPal's report doesn't.</summary>
    public string Match { get; set; } = "eshopOnly";
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<ReconciliationLocalPaymentDto> UnmatchedEshopPayments { get; set; } = new();
}

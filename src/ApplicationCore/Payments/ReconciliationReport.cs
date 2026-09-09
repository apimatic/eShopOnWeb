using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A reconciliation of PayPal's own transaction record against eShop's orders over a date range.
/// Anything PayPal knows about that eShop does not — or the reverse — is surfaced separately.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Total transactions PayPal returned for the range.</summary>
    public int PayPalTransactionCount { get; set; }

    /// <summary>PayPal transactions matched to an eShop order.</summary>
    public List<ReconciliationMatch> Matched { get; set; } = new();

    /// <summary>Transactions PayPal reported that could not be matched to any eShop order.</summary>
    public List<PayPalTransaction> InPayPalOnly { get; set; } = new();

    /// <summary>eShop captured orders with no matching PayPal transaction in the range.</summary>
    public List<ReconciliationOrderRef> InEShopOnly { get; set; } = new();
}

public class ReconciliationMatch
{
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public string PayPalTransactionId { get; set; } = string.Empty;
    public decimal OrderCapturedAmount { get; set; }
    public decimal PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public bool AmountsAgree { get; set; }
}

public class ReconciliationOrderRef
{
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

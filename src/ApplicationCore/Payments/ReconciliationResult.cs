using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Outcome of reconciling PayPal's transaction record against eShop orders for a range.</summary>
public record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCapturedOrderCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransaction> OnlyInPayPal,
    IReadOnlyList<EShopCapturedPayment> OnlyInEShop);

/// <summary>A PayPal transaction matched to an eShop order by a shared PayPal id.</summary>
public record ReconciliationMatch(string TransactionId, int OrderId, string? Status, decimal? Amount, string? Currency);

/// <summary>An eShop captured payment PayPal's report does not (yet) show for the range.</summary>
public record EShopCapturedPayment(int OrderId, string CaptureId, decimal? CapturedAmount, string? Currency);

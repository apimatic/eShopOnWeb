using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The identifiers this application puts on a payment so that PayPal's own record of the transaction
/// can be tied back to the order it belongs to, and so a retried request is recognisable.
/// </summary>
public static class PaymentReference
{
    /// <summary>
    /// Marker on everything this application sends to the processor. The payment's own id follows it,
    /// which is how a line on PayPal's statement is traced back to the payment that created it.
    /// </summary>
    public const string PREFIX = "eshop-pay";

    /// <summary>
    /// PayPal requires an invoice id that is unique per transaction - and refuses a payment that reuses
    /// one - so each hold gets its own, carrying the payment it belongs to and a per-payment token that
    /// keeps it unique across database restarts.
    /// </summary>
    public static string InvoiceId(int paymentId, string paymentReference, int attempt)
        => $"{Prefix(paymentId, paymentReference)}-hold-{attempt}";

    /// <summary>Stable per payment; carried as the processor's custom id and reported on statements.</summary>
    public static string CustomId(int paymentId, string paymentReference) => Prefix(paymentId, paymentReference);

    private static string Prefix(int paymentId, string paymentReference) => $"{PREFIX}-{paymentId}-{paymentReference}";

    public static string HoldRequestId(int paymentId, string paymentReference, int attempt)
        => $"{Prefix(paymentId, paymentReference)}-hold-{attempt}-req";

    public static string CaptureRequestId(int paymentId, string paymentReference, int renewalCount)
        => $"{Prefix(paymentId, paymentReference)}-capture-{renewalCount}";

    public static string RenewalRequestId(int paymentId, string paymentReference, int renewalCount)
        => $"{Prefix(paymentId, paymentReference)}-renew-{renewalCount}";

    public static string VoidRequestId(int paymentId, string paymentReference)
        => $"{Prefix(paymentId, paymentReference)}-void";

    /// <summary>
    /// The processor's own refund idempotency key. The caller's key is namespaced by the payment so two
    /// shoppers can use the same key without colliding at the processor.
    /// </summary>
    public static string RefundRequestId(int paymentId, string paymentReference, string callerKey)
        => $"{Prefix(paymentId, paymentReference)}-refund-{callerKey}";

    /// <summary>A stable, non-identifying key for the shopper's card vault at the processor.</summary>
    public static string ShopperVaultKey(string buyerId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(buyerId));
        return "eshop" + Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The invoice lifecycle states owned and reported by the payment provider (Visa/CyberSource).
/// eShop mirrors the provider's status verbatim; these constants exist so the rest of the
/// application can reason about the state without hard-coding string literals.
/// </summary>
public static class InvoiceStatus
{
    /// <summary>Raised with the provider but not yet put to the shopper.</summary>
    public const string Draft = "DRAFT";

    /// <summary>Published/live with the provider; payable but not delivered.</summary>
    public const string Created = "CREATED";

    /// <summary>Put to the shopper (delivered); payable.</summary>
    public const string Sent = "SENT";

    /// <summary>Partially paid.</summary>
    public const string Partial = "PARTIAL";

    /// <summary>Fully paid.</summary>
    public const string Paid = "PAID";

    /// <summary>Withdrawn; no longer payable.</summary>
    public const string Canceled = "CANCELED";

    /// <summary>Transient provider-side state.</summary>
    public const string Pending = "PENDING";

    /// <summary>
    /// A bill has been "put to the shopper" once it has left DRAFT — i.e. the provider has
    /// made it payable and can hand out a payment link for it.
    /// </summary>
    public static bool IsPutToShopper(string? status) =>
        !string.IsNullOrEmpty(status) &&
        !string.Equals(status, Draft, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>A withdrawn bill is one the provider reports as CANCELED.</summary>
    public static bool IsWithdrawn(string? status) =>
        string.Equals(status, Canceled, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A bill can still be corrected only while it is a DRAFT — once it has been put to the
    /// shopper or withdrawn, its details are frozen.
    /// </summary>
    public static bool CanBeCorrected(string? status) =>
        string.Equals(status, Draft, System.StringComparison.OrdinalIgnoreCase);
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The lifecycle states an invoice can be in at the provider. Mirrors the values the
/// Visa/CyberSource invoicing platform reports so that eShop tracks the same vocabulary
/// the provider owns rather than inventing a parallel one.
/// </summary>
public static class InvoiceStatus
{
    /// <summary>Raised but not yet put to the shopper. Still fully correctable.</summary>
    public const string Draft = "DRAFT";

    /// <summary>Put to the shopper and payable; a payment link is available.</summary>
    public const string Created = "CREATED";

    /// <summary>Put to the shopper and delivered (e.g. emailed); payable.</summary>
    public const string Sent = "SENT";

    /// <summary>Partially paid.</summary>
    public const string Partial = "PARTIAL";

    /// <summary>Fully paid.</summary>
    public const string Paid = "PAID";

    /// <summary>Withdrawn; no longer payable.</summary>
    public const string Canceled = "CANCELED";

    /// <summary>A transient provider-side processing state.</summary>
    public const string Pending = "PENDING";
}

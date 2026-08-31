using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The invoice lifecycle states owned by the provider (Visa / CyberSource). eShop mirrors these as
/// the last-known <see cref="Entities.InvoiceAggregate.Invoice.Status"/> rather than inventing its own.
/// </summary>
public static class InvoiceStatus
{
    public const string Draft = "DRAFT";
    public const string Created = "CREATED";
    public const string Sent = "SENT";
    public const string Partial = "PARTIAL";
    public const string Paid = "PAID";
    public const string Canceled = "CANCELED";
    public const string Pending = "PENDING";

    /// <summary>A freshly raised bill that has not yet been put to the shopper.</summary>
    public static bool IsDraft(string? status) =>
        string.Equals(status, Draft, StringComparison.OrdinalIgnoreCase);

    /// <summary>A bill that has been withdrawn and can no longer be paid.</summary>
    public static bool IsWithdrawn(string? status) =>
        string.Equals(status, Canceled, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A bill that has been put to the shopper: published (or beyond) at the provider and therefore
    /// payable, with a payment link available. Anything that is neither a draft nor withdrawn.
    /// </summary>
    public static bool IsIssued(string? status) =>
        !string.IsNullOrEmpty(status) && !IsDraft(status) && !IsWithdrawn(status);
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The invoice lifecycle states owned by the payment provider (Visa / CyberSource).
/// eShop mirrors the provider's own status strings verbatim rather than inventing a parallel
/// vocabulary, so what eShop reports always lines up with what the provider reports.
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
        string.Equals(status, Draft, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>A bill that has been withdrawn and can no longer be paid.</summary>
    public static bool IsWithdrawn(string? status) =>
        string.Equals(status, Canceled, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A bill that has been put to the shopper (or progressed beyond that). Once a bill is issued
    /// it is no longer a draft and can carry a payment link.
    /// </summary>
    public static bool IsIssued(string? status) =>
        !IsDraft(status) && !IsWithdrawn(status) && !string.IsNullOrWhiteSpace(status);

    /// <summary>
    /// Whether the provider will hand out a way to pay the bill. The provider only returns a
    /// payment link while the invoice is in one of these states.
    /// </summary>
    public static bool IsPayable(string? status) =>
        string.Equals(status, Created, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Sent, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Partial, System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Paid, System.StringComparison.OrdinalIgnoreCase);
}

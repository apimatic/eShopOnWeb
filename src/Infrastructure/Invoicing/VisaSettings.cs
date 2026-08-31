namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration. <see cref="BaseUrl"/> is bound
/// from non-secret configuration; the credentials are loaded from .NET user-secrets and never written
/// into the repository.
/// </summary>
public class VisaSettings
{
    public const string CONFIG_NAME = "Visa";

    /// <summary>
    /// The base address every call to Visa is routed through. Used verbatim in place of any default
    /// the SDK would otherwise use, so the same build can run against a different address.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Merchant id (from the VISA_MERCHANT_ID secret).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Shared-secret key id (from the VISA_KEY_ID secret).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Shared-secret key (from the VISA_SECRET_KEY secret). Never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Overall provider request timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; set; } = 60000;

    /// <summary>Page size used when paginating the provider's invoice list during reconciliation.</summary>
    public int ListPageSize { get; set; } = 100;

    /// <summary>How many invoice-detail reads to run concurrently while reconciling.</summary>
    public int ReconciliationConcurrency { get; set; } = 6;

    /// <summary>
    /// Safety cap on how many provider invoices reconciliation will scan, protecting against an
    /// ever-growing shared sandbox. Zero or negative means no cap.
    /// </summary>
    public int ReconciliationMaxInvoices { get; set; } = 2000;
}

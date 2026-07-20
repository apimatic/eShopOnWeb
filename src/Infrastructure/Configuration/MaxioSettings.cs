namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>).
/// Bound from the "Maxio" configuration section (user-secrets / appsettings / environment).
/// </summary>
public class MaxioSettings
{
    /// <summary>API key, used as the HTTP Basic-auth username. Never logged or committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain (e.g. "apimatic-hackathon") — used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region: "US" or "EU". Not the deployment target — see <see cref="BaseUrl"/>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins over the
    /// Subdomain-derived host, so the same build can target production, a dev/sandbox
    /// tenant, or a local mock server purely through configuration.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    // The sandbox's eshop-pro/basic-plan products require a payment method at signup (confirmed
    // live; maxio-plan.md §2.2a). The SDK/map document no concrete sandbox test-card number, so
    // this is a generic, widely-used industry test-card value (Luhn-valid, non-sensitive — not a
    // real financial instrument, so it is not a "credential" and is not secret) — override via
    // configuration if the sandbox's connected gateway needs a different test value.
    public string TestCreditCardNumber { get; set; } = "4111111111111111";
    public int TestCreditCardExpirationMonth { get; set; } = 12;
    public int TestCreditCardExpirationYear { get; set; } = 2032;
    public string TestCreditCardCvv { get; set; } = "123";
}

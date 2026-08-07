using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal</c> configuration section. The secret values
/// are supplied via .NET user-secrets / environment variables and never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>The sandbox base URL taken from PayPal's OpenAPI specs (the only server they declare).</summary>
    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Always <c>sandbox</c> for this integration.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional explicit base URL. When set, it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> override if present, otherwise
    /// the base URL derived from <see cref="Environment"/> using the value declared in the specs.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var environment = Environment?.Trim();
        if (string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return SandboxBaseUrl;
        }

        throw new InvalidOperationException(
            $"PayPal base URL cannot be resolved for environment '{Environment}'. Set 'PayPal:Environment' to " +
            "'sandbox', or provide an explicit 'PayPal:BaseUrl'.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        }
        // Surfaces a misconfigured environment/base-url early.
        _ = ResolveBaseUrl();
    }
}

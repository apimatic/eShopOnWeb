using System;
using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. No value
/// is hard-coded — the same build runs against a different PayPal account by supplying different
/// configuration. Credentials come from user-secrets / environment; nothing is written to the repo.
/// </summary>
public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The PayPal environment name, e.g. <c>sandbox</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>The three-letter ISO-4217 currency all amounts are charged in, e.g. <c>USD</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the credential/token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maps the configured <see cref="Environment"/> onto a known SDK <see cref="ServerEnvironment"/>.
    /// Returns <c>null</c> when the value is not a supported environment, so the host can fail fast
    /// rather than silently sending test traffic somewhere unexpected.
    /// </summary>
    public ServerEnvironment? ResolveEnvironment()
    {
        if (string.Equals(Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Sandbox;
        }

        // The SDK declares no other environment; anything else is unsupported.
        return null;
    }
}

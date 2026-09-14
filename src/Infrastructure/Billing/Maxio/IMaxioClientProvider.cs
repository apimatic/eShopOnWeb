using MaxioAdvancedBilling;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Supplies the configured, long-lived Maxio SDK client.
/// </summary>
/// <remarks>
/// The client is resolved through a provider rather than injected directly so that invalid configuration
/// surfaces as a <see cref="ApplicationCore.Exceptions.BillingNotConfiguredException"/> on the first billing
/// request, instead of throwing while the service provider is being built and taking the whole API down.
/// </remarks>
public interface IMaxioClientProvider
{
    /// <summary>
    /// Returns the shared client, building it on first use.
    /// </summary>
    /// <exception cref="ApplicationCore.Exceptions.BillingNotConfiguredException">
    /// The <c>Maxio</c> configuration section is missing or invalid.
    /// </exception>
    MaxioAdvancedBillingClient GetClient();
}

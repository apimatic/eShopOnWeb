namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A handle this integration is configured against does not resolve to the expected entity on the
/// billing provider — the sandbox was re-seeded, the handle was renamed, or the entity was archived.
/// </summary>
/// <remarks>
/// This is deliberately distinct from a transport failure: the fix is to correct the seed (plan.md
/// UC0) or the configuration, never to retry or to guess a different entity.
/// </remarks>
public class BillingConfigurationException : BillingProviderException
{
    public BillingConfigurationException(string message)
        : base($"{message} Check the Maxio configuration and the provider seed (see plan.md UC0).")
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing configuration is missing or points at entities that do not exist -
/// a stale handle after a sandbox reseed, or an absent API key. Never carries a secret value.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

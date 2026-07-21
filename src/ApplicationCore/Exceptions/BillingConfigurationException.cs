using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a configured plan/component handle does not resolve against the billing provider
/// (e.g. the sandbox was reseeded and the ids/handles drifted). Points the operator back at UC0.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

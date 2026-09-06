using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Subscription billing is not usable because the deployment is missing or misconfigured settings.
/// This is an operator problem, never a caller problem.
/// </summary>
public class SubscriptionConfigurationException : Exception
{
    public SubscriptionConfigurationException(string message) : base(message)
    {
    }
}

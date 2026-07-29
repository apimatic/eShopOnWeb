using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing integration is misconfigured (e.g. a required Maxio setting is
/// missing). Surfaces as HTTP 503 Service Unavailable: the capability cannot operate until
/// configuration is fixed. Validated lazily on first use so the app still boots without
/// billing configured.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message)
        : base(message)
    {
    }
}

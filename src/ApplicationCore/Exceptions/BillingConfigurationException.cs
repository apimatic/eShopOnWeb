using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A configured handle does not resolve on the billing provider, or resolves to an entity of the
/// wrong shape. This is a seeding/configuration fault, not a customer fault — correct the seed
/// rather than retrying the customer's action.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The billing request cannot be carried out; carries the HTTP status to answer with and the reason.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string reason) : base(reason)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Maxio is not (correctly) configured for this deployment.</summary>
public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string reason) : base(503, "Billing is not available: " + reason)
    {
    }
}

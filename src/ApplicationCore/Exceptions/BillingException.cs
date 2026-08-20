using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException()
        : base("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain or Maxio:BaseUrl, and Maxio:ProductFamilyHandle.", 503)
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' is available.", 404)
    {
    }
}

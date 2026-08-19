using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 500) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message, 503)
    {
    }
}

public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.", 404)
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

public class SubscriptionEnrollmentException : BillingException
{
    public SubscriptionEnrollmentException(string message) : base(message, 400)
    {
    }
}

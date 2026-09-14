using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The Maxio integration is not configured correctly (missing credentials, unknown product
/// family, rejected credentials). The caller cannot act on this; it is a server configuration error.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing provider could not be reached, timed out, or returned a response that could not be
/// processed. The caller may retry later.
/// </summary>
public class MaxioUnavailableException : Exception
{
    public MaxioUnavailableException(string message) : base(message)
    {
    }

    public MaxioUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>No subscribable plan with the requested handle exists in the configured product family.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' was not found in the configured product family.")
    {
    }
}

/// <summary>
/// The billing provider rejected the request (e.g. the plan cannot be subscribed without a payment
/// method). The caller sent something the provider will not accept, so retrying will not help.
/// </summary>
public class MaxioRequestRejectedException : Exception
{
    public MaxioRequestRejectedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown by <see cref="MaxioSingleSendHandler"/> when an SDK retry would re-send a write request.
/// The first attempt may already have reached the provider, so callers must reconcile provider state.
/// </summary>
internal sealed class MaxioResendRefusedException : Exception
{
    public MaxioResendRefusedException()
        : base("A billing write request was refused to guarantee at-most-once delivery.")
    {
    }
}

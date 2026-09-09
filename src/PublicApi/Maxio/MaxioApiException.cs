using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API responds with a non-success status.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Raised when the requested subscription plan (Maxio product) cannot be found in the
/// configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public PlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}

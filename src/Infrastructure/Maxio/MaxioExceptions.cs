using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

public class MaxioPlanNotFoundException : Exception
{
    public string ProductHandle { get; }

    public MaxioPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' exists in the configured Maxio product family.")
    {
        ProductHandle = productHandle;
    }
}

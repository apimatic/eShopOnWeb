using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

public class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message)
    {
    }
}

public class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

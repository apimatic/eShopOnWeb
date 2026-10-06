using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures coming out of the billing system of record (Maxio).
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

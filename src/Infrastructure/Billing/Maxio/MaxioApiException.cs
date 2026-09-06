using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A call to Maxio Advanced Billing failed.
/// </summary>
public class MaxioApiException : BillingGatewayException
{
    public MaxioApiException(
        string message,
        int? statusCode = null,
        IReadOnlyList<string>? errors = null,
        bool isDuplicateReference = false,
        Exception? innerException = null)
        : base(message, statusCode, errors, isDuplicateReference, innerException)
    {
    }
}

using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio call that did not succeed. Carries the operation that failed and the provider's own error
/// messages, parsed from the error models the specification declares
/// (<c>Error-List-Response.yaml</c> and <c>Customer-Error-Response.yaml</c>).
/// </summary>
public class MaxioApiException : BillingProviderException
{
    public MaxioApiException(
        string operationId,
        int? statusCode,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(BuildMessage(operationId, statusCode), statusCode, errors, innerException)
    {
        OperationId = operationId;
    }

    /// <summary>The <c>operationId</c> from the specification for the call that failed.</summary>
    public string OperationId { get; }

    private static string BuildMessage(string operationId, int? statusCode) =>
        statusCode.HasValue
            ? $"Maxio operation '{operationId}' failed with HTTP {statusCode.Value}."
            : $"Maxio operation '{operationId}' could not be completed.";
}

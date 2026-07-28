using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio rejects a request with a validation error (HTTP 422). The <see cref="MaxioApiException.Errors"/>
/// carry the human readable reasons. These represent a bad request the caller could correct.
/// </summary>
public class MaxioValidationException : MaxioApiException
{
    public MaxioValidationException(string message, IReadOnlyList<string> errors)
        : base(message, statusCode: 422, errors: errors)
    {
    }
}

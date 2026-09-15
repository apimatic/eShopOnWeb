using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioValidationException : MaxioApiException
{
    public MaxioValidationException(IReadOnlyList<string> errors)
        : base(422, errors)
    {
    }
}

using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioDuplicateException : MaxioApiException
{
    public MaxioDuplicateException(IReadOnlyList<string> errors)
        : base(409, errors)
    {
    }
}

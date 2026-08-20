using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioDuplicateSubmissionException : Exception
{
    public MaxioDuplicateSubmissionException()
        : base("Maxio rejected a duplicate submission (uniqueness_token).")
    {
    }
}

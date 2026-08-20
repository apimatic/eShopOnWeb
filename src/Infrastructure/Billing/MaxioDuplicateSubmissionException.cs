using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioDuplicateSubmissionException : Exception
{
    public MaxioDuplicateSubmissionException(string uniquenessToken)
        : base("Maxio rejected a duplicate submission.")
    {
        UniquenessToken = uniquenessToken;
    }

    public string UniquenessToken { get; }
}

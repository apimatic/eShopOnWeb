using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Signals that a customer with the requested reference already exists (Maxio 422 on create).</summary>
internal sealed class MaxioDuplicateReferenceException : Exception
{
    public MaxioDuplicateReferenceException(string reference)
        : base($"A Maxio customer with reference '{reference}' already exists.")
    {
    }
}

/// <summary>
/// Signals that Maxio rejected a POST as a duplicate submission because the same uniqueness token was
/// seen very recently (Maxio 409). Used to recover the winning record.
/// </summary>
internal sealed class MaxioDuplicateSubmissionException : Exception
{
    public MaxioDuplicateSubmissionException()
        : base("Maxio rejected the request as a duplicate submission (409 Conflict).")
    {
    }
}

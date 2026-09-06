using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio answered 422: the request was understood but rejected. Whether that is the caller's
/// mistake or a race the integration can recover from depends on the reported messages, so the
/// workflow layer inspects <see cref="BillingValidationException.Errors"/> before deciding.
/// </summary>
internal class MaxioUnprocessableEntityException : BillingValidationException
{
    public MaxioUnprocessableEntityException(string message, IReadOnlyList<string> errors)
        : base(message, errors)
    {
    }
}

/// <summary>
/// Maxio answered 409: an identical request carrying the same uniqueness token was already
/// received. The first submission may or may not have succeeded, so the workflow re-reads state
/// rather than assuming either outcome.
/// </summary>
internal class MaxioDuplicateSubmissionException : BillingException
{
    public MaxioDuplicateSubmissionException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio rejected a write because an identical uniqueness token was submitted within the last
/// 60 minutes. The original request may or may not have succeeded, so the caller has to re-read
/// state before deciding what to do.
/// </summary>
internal sealed class MaxioDuplicateSubmissionException : SubscriptionBillingException
{
    public MaxioDuplicateSubmissionException(string message, IEnumerable<string>? errors = null)
        : base(message, 409, errors)
    {
    }
}

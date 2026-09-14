using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider refuses a create because the reference eShopOnWeb supplied is
/// already taken. This is the provider-side half of the idempotency guarantee: it means a concurrent
/// request already created the record, so the caller should re-read rather than retry the create.
/// </summary>
public class BillingReferenceConflictException : BillingProviderException
{
    public BillingReferenceConflictException(string reference, IEnumerable<string>? providerErrors = null)
        : base($"The billing reference '{reference}' is already in use.", providerErrors)
    {
        Reference = reference;
    }

    public string Reference { get; }
}

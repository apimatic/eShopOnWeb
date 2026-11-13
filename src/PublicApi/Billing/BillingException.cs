using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>What the caller of the subscription API should be told. Each kind maps to exactly one HTTP status.</summary>
public enum BillingErrorKind
{
    /// <summary>400 - the request itself is invalid (unknown plan, missing field).</summary>
    InvalidRequest,

    /// <summary>409 - conflicts with the account's current state (in progress, already subscribed to another plan).</summary>
    Conflict,

    /// <summary>422 - Maxio refused the request for a reason the caller can act on.</summary>
    Rejected,

    /// <summary>502 - our billing configuration or credentials are wrong; the caller cannot fix it.</summary>
    ProviderMisconfigured,

    /// <summary>503 - billing is temporarily unavailable; nothing was changed and the request can be retried.</summary>
    Unavailable
}

/// <summary>A caller-safe billing failure. The message never carries SDK internals, URLs or personal data.</summary>
public sealed class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Provider validation messages the caller may act on (only for <see cref="BillingErrorKind.Rejected"/>).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>The one mapping from a gateway failure to what the caller sees.</summary>
    public static BillingException From(MaxioGatewayException ex) => ex.Kind switch
    {
        MaxioFailureKind.Rejected => new BillingException(BillingErrorKind.Rejected, "The billing provider rejected the request.", ex.ProviderErrors, ex),
        MaxioFailureKind.Configuration or MaxioFailureKind.NotFound =>
            new BillingException(BillingErrorKind.ProviderMisconfigured, "Billing is not available: the billing provider configuration was refused.", null, ex),
        _ => new BillingException(BillingErrorKind.Unavailable, "Billing is temporarily unavailable. Nothing was changed; please retry shortly.", null, ex)
    };
}

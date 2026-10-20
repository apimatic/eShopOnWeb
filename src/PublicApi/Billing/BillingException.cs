using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public enum BillingErrorKind
{
    /// <summary>The caller sent something invalid, e.g. a plan this shop does not offer.</summary>
    InvalidRequest,

    /// <summary>The billing provider rejected the request on its merits (validation errors).</summary>
    Rejected,

    /// <summary>The same request is already being processed.</summary>
    InProgress,

    /// <summary>The provider may have acted but the outcome is not confirmed yet; it is settled on a later call.</summary>
    OutcomePending,

    /// <summary>The plan catalogue could not be read completely, so the plan could not be checked.</summary>
    CatalogIncomplete,

    /// <summary>The provider is throttling this application.</summary>
    ProviderBusy,

    /// <summary>The provider could not be reached, refused our credentials, or answered unusably.</summary>
    ProviderUnavailable,

    /// <summary>The provider did not answer within the time budget.</summary>
    Timeout
}

/// <summary>
/// A billing failure whose message is safe to return to the caller. It never carries provider exception text.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Errors = errors ?? Array.Empty<string>();
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Validation messages returned by the provider, when <see cref="Kind"/> is <see cref="BillingErrorKind.Rejected"/>.</summary>
    public IReadOnlyList<string> Errors { get; }

    public HttpStatusCode StatusCode => Kind switch
    {
        BillingErrorKind.InvalidRequest => HttpStatusCode.BadRequest,
        BillingErrorKind.Rejected => HttpStatusCode.UnprocessableEntity,
        BillingErrorKind.InProgress => HttpStatusCode.Conflict,
        BillingErrorKind.OutcomePending => HttpStatusCode.ServiceUnavailable,
        BillingErrorKind.CatalogIncomplete => HttpStatusCode.ServiceUnavailable,
        BillingErrorKind.ProviderBusy => HttpStatusCode.ServiceUnavailable,
        BillingErrorKind.Timeout => HttpStatusCode.GatewayTimeout,
        _ => HttpStatusCode.BadGateway
    };

    /// <summary>Seconds after which a retry is sensible, for the failures a retry can fix.</summary>
    public int? RetryAfterSeconds => Kind switch
    {
        BillingErrorKind.InProgress => 5,
        BillingErrorKind.OutcomePending => 30,
        BillingErrorKind.ProviderBusy => 30,
        _ => null
    };
}

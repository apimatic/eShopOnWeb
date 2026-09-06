using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected a call or could not be reached.
/// </summary>
public class BillingGatewayException : BillingException
{
    public BillingGatewayException(
        string message,
        int? statusCode = null,
        IReadOnlyList<string>? errors = null,
        bool isDuplicateReference = false,
        Exception? innerException = null)
        : base(message, innerException!)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
        IsDuplicateReference = isDuplicateReference;
    }

    /// <summary>HTTP status the billing system returned, or null when the call never completed.</summary>
    public int? StatusCode { get; }

    /// <summary>Validation messages the billing system reported, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the call failed only because the reference we sent is already taken -
    /// i.e. a concurrent request created the record first. Callers can recover by
    /// re-reading the record that won the race.
    /// </summary>
    public bool IsDuplicateReference { get; }

    /// <summary>
    /// True when the failure is on our side of the contract (4xx other than 429) and so
    /// retrying the identical request will not help.
    /// </summary>
    public bool IsClientError => StatusCode is >= 400 and < 500 and not 429;

    public string ToDetailMessage() =>
        Errors.Count == 0 ? Message : $"{Message} ({string.Join("; ", Errors.Take(10))})";
}

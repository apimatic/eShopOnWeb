using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of validating a caller-supplied number against the provider.</summary>
/// <param name="IsUsable">Whether the provider considers the number a usable destination.</param>
/// <param name="CanonicalE164">The provider's canonical E.164 form (only when usable).</param>
public record PhoneValidationResult(bool IsUsable, string? CanonicalE164);

/// <summary>A message as the provider reports it, from a send or a later fetch.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>A message row from the provider's own list, used for reconciliation.</summary>
public record ProviderMessageSummary(
    string Sid,
    string Status,
    string? To,
    DateTimeOffset? DateSent);

/// <summary>The provider's own record of messages over a range (possibly truncated at a page cap).</summary>
public record ProviderMessageListing(
    IReadOnlyList<ProviderMessageSummary> Messages,
    bool Truncated,
    int PagesRead);

/// <summary>
/// A provider failure translated at the gateway boundary — the single failure type the rest of the
/// app handles. <see cref="StatusCode"/> is present for provider HTTP errors, absent for transport
/// failures and unknown outcomes.
/// </summary>
public class ProviderGatewayException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public ProviderGatewayException(string message, Exception? inner = null, HttpStatusCode? statusCode = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

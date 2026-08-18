using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Outcome of registering a contact number.</summary>
public sealed record ContactNumberRegistration(bool Accepted, int? ContactNumberId, string? CanonicalE164, string? RejectionReason);

public enum ResendStatus
{
    /// <summary>A new message was sent; <see cref="ResendOutcome.NotificationId"/> is its notification.</summary>
    Sent,
    /// <summary>The idempotency key was already used; the prior notification is returned, nothing re-sent.</summary>
    ReusedIdempotent,
    /// <summary>No notification with the given id exists.</summary>
    SourceNotFound,
    /// <summary>The source message's content was disposed of, so there is nothing to re-send.</summary>
    ContentUnavailable
}

/// <summary>Outcome of a resend request.</summary>
public sealed record ResendOutcome(ResendStatus Status, int NotificationId);

/// <summary>A single message lined up across the provider's record and eShop's own.</summary>
public sealed record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? ProviderDateSent,
    int? OrderId);

/// <summary>
/// The reconciliation report over a date range: the provider's own record of messages from this
/// application's sending number, lined up against what eShop believes it sent.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>Raised when a placed order references a catalog item id that does not exist.</summary>
public sealed class UnknownCatalogItemException : Exception
{
    public UnknownCatalogItemException(int catalogItemId)
        : base($"Catalog item {catalogItemId} does not exist.")
    {
    }
}

/// <summary>
/// Maps an <see cref="SmsGatewayException"/> to a caller-facing result. A provider failure that is
/// really OUR problem (bad credentials, spent quota) or a transport failure is a 5xx to the caller;
/// a genuine caller-fixable 4xx from the provider is surfaced as that status.
/// </summary>
public static class SmsErrorResults
{
    public static IResult ToResult(SmsGatewayException ex)
    {
        int status = ex.StatusCode switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,   // our credentials — caller can't fix
            429 => StatusCodes.Status503ServiceUnavailable,  // our quota
            >= 400 and < 500 => ex.StatusCode!.Value,        // the provider rejected the request itself
            _ => StatusCodes.Status502BadGateway             // transport / provider 5xx / unknown
        };

        return Results.Json(new { message = ex.Message }, statusCode: status);
    }
}

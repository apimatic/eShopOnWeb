using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's record of messages sent from this application's configured sending number over
/// a date range, lined up against what eShop believes it sent. Anything the provider knows about
/// that eShop does not — or the reverse — is surfaced.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<EShopOnlyNotification> EShopOnly);

/// <summary>A message the provider and eShop both know about, matched on the provider's identifier.</summary>
public record ReconciliationMatch(
    string ProviderMessageId,
    string ProviderStatus,
    int NotificationId,
    int OrderId,
    string EShopStatus);

/// <summary>A message the provider recorded that eShop has no notification for.</summary>
public record ProviderOnlyMessage(
    string ProviderMessageId,
    string ProviderStatus,
    DateTimeOffset? DateSent);

/// <summary>A notification eShop believes it sent that the provider's record does not include.</summary>
public record EShopOnlyNotification(
    int NotificationId,
    int OrderId,
    string? ProviderMessageId,
    string EShopStatus);

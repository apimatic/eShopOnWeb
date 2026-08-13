using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the provider's own record of messages sent from eShop's configured sending number
/// over a date range against what eShop believes it sent, so a message the provider knows about
/// and eShop doesn't — or the reverse — is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderOnly> ProviderOnly,
    IReadOnlyList<ReconciliationEShopOnly> EShopOnly);

/// <summary>A message both the provider and eShop have a record of, matched by provider SID.</summary>
public record ReconciliationMatch(
    string ProviderSid,
    int NotificationId,
    string ProviderStatus,
    string EShopStatus,
    DateTimeOffset? DateSent);

/// <summary>A message the provider has a record of that eShop cannot account for.</summary>
public record ReconciliationProviderOnly(
    string ProviderSid,
    string ProviderStatus,
    DateTimeOffset? DateSent);

/// <summary>A message eShop recorded sending that the provider did not return for the range.</summary>
public record ReconciliationEShopOnly(
    int NotificationId,
    string ProviderSid,
    string EShopStatus);

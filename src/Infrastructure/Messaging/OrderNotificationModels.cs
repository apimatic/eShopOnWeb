using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    DateTimeOffset? LastRefreshedAt,
    bool StatusIsStale,
    bool ContentDisposed);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

public sealed record ReconciliationItem(
    string MatchState,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? ProviderDate);

public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationItem> Messages);

using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed record ContactNumberView(int ContactNumberId, string CanonicalNumber, DateTimeOffset CreatedAt);

public sealed record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentRedactedAt,
    int? OriginalNotificationId);

public sealed record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<NotificationView> Notifications);

public sealed record ReconciliationEntry(
    string Comparison,
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);

public enum OperationOutcome
{
    Success,
    NotFound,
    Conflict,
    ProviderUnavailable
}

public sealed record OperationResult(OperationOutcome Outcome, int? Identifier = null, string? Error = null);

public sealed class ContactNumberValidationException : Exception
{
    public ContactNumberValidationException(IReadOnlyList<string> validationErrors)
        : base("The messaging provider does not consider this a valid destination.")
    {
        ValidationErrors = validationErrors;
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}

public sealed class OrderRequestValidationException : Exception
{
    public OrderRequestValidationException(string message) : base(message) { }
}

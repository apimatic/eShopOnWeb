using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>A phone number the provider has validated, in the provider's canonical form.</summary>
public sealed record ValidatedPhoneNumber(string CanonicalNumber, string? NationalFormat);

/// <summary>The provider-facing outcome of a single message operation.</summary>
public sealed record TextMessageResult(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>The provider's own record of a message, used for reconciliation.</summary>
public sealed record ProviderTextMessage(
    string Sid,
    string? From,
    string? To,
    string? Status,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? ErrorCode);

/// <summary>A catalog item and quantity requested for an order.</summary>
public sealed record OrderItemRequest(int CatalogItemId, int Quantity);

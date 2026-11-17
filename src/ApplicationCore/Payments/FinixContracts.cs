using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class FinixBuyerIdentityRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public FinixAddress? Address { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}

public class FinixCreatePaymentInstrumentRequest
{
    public string IdentityId { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinixAddress? Address { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}

public class FinixAuthorizationRequest
{
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string MerchantId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string IdempotencyId { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}

public class FinixIdentityResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class FinixPaymentInstrumentResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? Last4 { get; set; }

    [JsonPropertyName("expiration_month")]
    public int? ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int? ExpirationYear { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("instrument_type")]
    public string? InstrumentType { get; set; }
}

public class FinixAuthorizationResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long AmountCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("transfer")]
    public string? TransferId { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, JsonElement>? Tags { get; set; }
}

public class FinixTransferResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long AmountCents { get; set; }

    [JsonPropertyName("amount_requested")]
    public long? AmountRequestedCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("fee")]
    public long? FeeCents { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransferId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, JsonElement>? Tags { get; set; }
}

public class FinixPage
{
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public class FinixLink
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

public class FinixLinks
{
    [JsonPropertyName("self")]
    public FinixLink? Self { get; set; }

    [JsonPropertyName("next")]
    public FinixLink? Next { get; set; }
}

public class FinixEmbedded<T>
{
    [JsonPropertyName("authorizations")]
    public List<T>? Authorizations { get; set; }

    [JsonPropertyName("transfers")]
    public List<T>? Transfers { get; set; }
}

public class FinixListEnvelope<T>
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixEmbedded<T>? Embedded { get; set; }

    [JsonPropertyName("_links")]
    public FinixLinks? Links { get; set; }
}

public class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string? code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}

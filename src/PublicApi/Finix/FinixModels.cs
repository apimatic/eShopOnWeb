using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed record FinixAddress(
    [property: JsonPropertyName("line1")] string Line1,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("line2")] string? Line2 = null);

public sealed record FinixCardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    FinixAddress Address);

public sealed class CreateIdentityRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "PERSONAL";

    [JsonPropertyName("identity_roles")]
    public string[] IdentityRoles { get; set; } = { "BUYER" };

    [JsonPropertyName("entity")]
    public CreateIdentityEntity Entity { get; set; } = new();
}

public sealed class CreateIdentityEntity
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("personal_address")]
    public FinixAddress PersonalAddress { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, "USA");
}

public sealed class CreatePaymentInstrumentRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "PAYMENT_CARD";

    [JsonPropertyName("identity")]
    public string Identity { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("address")]
    public FinixAddress? Address { get; set; }
}

public sealed class CreateAuthorizationRequest
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("merchant")]
    public string Merchant { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("idempotency_id")]
    public string IdempotencyId { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class CaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")]
    public long CaptureAmount { get; set; }
}

public sealed class VoidAuthorizationRequest
{
    [JsonPropertyName("void_me")]
    public bool VoidMe { get; set; } = true;
}

public sealed class CreateRefundRequest
{
    [JsonPropertyName("refund_amount")]
    public long RefundAmount { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string IdempotencyId { get; set; } = string.Empty;
}

public sealed class UpdatePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class FinixIdentityResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public sealed class FinixPaymentInstrumentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int? ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int? ExpirationYear { get; set; }

    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }

    [JsonPropertyName("instrument_type")]
    public string? InstrumentType { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("disabled_code")]
    public string? DisabledCode { get; set; }

    [JsonPropertyName("disabled_message")]
    public string? DisabledMessage { get; set; }
}

public sealed class FinixAuthorizationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("is_void")]
    public bool? IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("amount")]
    public long? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixTransferResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long? Amount { get; set; }

    [JsonPropertyName("fee")]
    public long? Fee { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixTransferListEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public sealed class FinixTransferListEmbedded
{
    [JsonPropertyName("transfers")]
    public List<FinixTransferResponse> Transfers { get; set; } = new();
}

public sealed class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixAuthorizationListEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public sealed class FinixAuthorizationListEmbedded
{
    [JsonPropertyName("authorizations")]
    public List<FinixAuthorizationResponse> Authorizations { get; set; } = new();
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public sealed class FinixErrorResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

public sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError> Errors { get; set; } = new();
}

public sealed class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("logref")]
    public string? LogRef { get; set; }
}

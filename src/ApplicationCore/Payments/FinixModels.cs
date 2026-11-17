using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixIdentityResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class FinixPaymentInstrumentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("instrument_type")]
    public string? InstrumentType { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }
}

public class FinixAuthorizationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

public class FinixTransferResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("fee")]
    public int Fee { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }
}

public class FinixListResponse<T>
{
    [JsonPropertyName("_embedded")]
    public FinixEmbeddedList<T>? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public class FinixEmbeddedList<T>
{
    [JsonPropertyName("transfers")]
    public List<T>? Transfers { get; set; }

    [JsonPropertyName("authorizations")]
    public List<T>? Authorizations { get; set; }
}

public class FinixPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public class FinixCreatePaymentInstrumentRequest
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

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixAddress
{
    [JsonPropertyName("line1")]
    public string? Line1 { get; set; }

    [JsonPropertyName("line2")]
    public string? Line2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

public class FinixCreateIdentityRequest
{
    [JsonPropertyName("entity")]
    public object Entity { get; set; } = new { };

    [JsonPropertyName("identity_roles")]
    public string[] IdentityRoles { get; set; } = new[] { "BUYER" };

    [JsonPropertyName("type")]
    public string Type { get; set; } = "PERSONAL";
}

public class FinixCreateAuthorizationRequest
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("merchant")]
    public string Merchant { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixCaptureRequest
{
    [JsonPropertyName("capture_amount")]
    public int CaptureAmount { get; set; }
}

public class FinixVoidRequest
{
    [JsonPropertyName("void_me")]
    public bool VoidMe { get; set; } = true;
}

public class FinixReversalRequest
{
    [JsonPropertyName("refund_amount")]
    public int RefundAmount { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Finix;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" => "https://finix.live-payments-api.com/",
            _ => "https://finix.sandbox-payments-api.com/"
        };
    }

    public string ResolveCurrency()
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentException(
                "Finix:Currency is not configured. Set Finix:Currency (or FINIX_CURRENCY).",
                500, "FINIX_NOT_CONFIGURED");
        }

        return Currency.Trim().ToUpperInvariant();
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(MerchantId) || string.IsNullOrWhiteSpace(Currency))
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentException(
                "Finix is not configured. Set Finix:Username, Finix:Password, Finix:MerchantId and Finix:Currency (or the FINIX_USERNAME, FINIX_PASSWORD, FINIX_MERCHANT_ID and FINIX_CURRENCY environment variables).",
                500, "FINIX_NOT_CONFIGURED");
        }
    }
}

internal class FinixIdentityRequest
{
    [JsonPropertyName("entity")] public object Entity { get; set; } = new { };
    [JsonPropertyName("identity_roles")] public string[]? IdentityRoles { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

internal class FinixAddress
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

internal class FinixCreatePaymentInstrumentRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "PAYMENT_CARD";
    [JsonPropertyName("identity")] public string Identity { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("number")] public string Number { get; set; } = string.Empty;
    [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("address")] public FinixAddress? Address { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

internal class FinixCreateAuthorizationRequest
{
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("merchant")] public string Merchant { get; set; } = string.Empty;
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

internal class FinixCaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")] public long CaptureAmount { get; set; }
}

internal class FinixVoidAuthorizationRequest
{
    [JsonPropertyName("void_me")] public bool VoidMe { get; set; } = true;
}

internal class FinixCreateReversalRequest
{
    [JsonPropertyName("refund_amount")] public long RefundAmount { get; set; }
}

internal class FinixDisablePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
}

public class FinixIdentity
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

public class FinixPaymentInstrument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_four")] public string? LastFour { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYear { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
}

public class FinixAuthorization
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("is_void")] public bool? IsVoid { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("amount")] public long? Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixTransfer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("amount")] public long? Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("fee")] public long? Fee { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixFee
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public decimal? Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("linked_to")] public string? LinkedTo { get; set; }
    [JsonPropertyName("linked_type")] public string? LinkedType { get; set; }
    [JsonPropertyName("fee_type")] public string? FeeType { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

internal class FinixLink
{
    [JsonPropertyName("href")] public string? Href { get; set; }
}

internal class FinixLinks
{
    [JsonPropertyName("self")] public FinixLink? Self { get; set; }
    [JsonPropertyName("next")] public FinixLink? Next { get; set; }
}

internal class FinixPage
{
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

internal class FinixListResponse<T>
{
    [JsonPropertyName("_embedded")] public Dictionary<string, List<T>>? Embedded { get; set; }
    [JsonPropertyName("_links")] public FinixLinks? Links { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

internal class FinixErrorEnvelope
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("_embedded")] public FinixErrorContainer? Embedded { get; set; }
}

internal class FinixErrorContainer
{
    [JsonPropertyName("errors")] public List<FinixError>? Errors { get; set; }
}

internal class FinixError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

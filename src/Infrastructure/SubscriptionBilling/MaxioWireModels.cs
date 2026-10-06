using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

/// <summary>
/// Wire-format DTOs mirroring the Maxio Advanced Billing JSON responses.
/// Kept separate from the domain models so provider fields can drift freely.
/// </summary>
internal static class MaxioWireModels
{
    /// <summary>Top-level wrapper for endpoints that return a single customer.</summary>
    public class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public WireCustomer? Customer { get; set; }
    }

    /// <summary>Element wrapper used by product list endpoints: [{"product": {...}}].</summary>
    public class ProductResponse
    {
        [JsonPropertyName("product")]
        public WireProduct? Product { get; set; }
    }

    /// <summary>Top-level wrapper for subscription endpoints and element wrapper for lists.</summary>
    public class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public WireSubscription? Subscription { get; set; }
    }

    public class WireCustomer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    public class WireProduct
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("price_in_cents")]
        public long PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 1;

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; set; }

        [JsonPropertyName("archived_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    public class WireSubscription
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("product_price_in_cents")]
        public long ProductPriceInCents { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string? PaymentCollectionMethod { get; set; }

        [JsonPropertyName("current_period_started_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

        [JsonPropertyName("current_period_ends_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

        [JsonPropertyName("next_assessment_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? NextAssessmentAt { get; set; }

        [JsonPropertyName("activated_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? ActivatedAt { get; set; }

        [JsonPropertyName("created_at")]
        [JsonConverter(typeof(MaxioNullableDateTimeOffsetConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("customer")]
        public WireSubscriptionCustomerRef? Customer { get; set; }

        [JsonPropertyName("product")]
        public WireProduct? Product { get; set; }
    }

    /// <summary>Customer as nested in a subscription payload (id is what we need).</summary>
    public class WireSubscriptionCustomerRef
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    /// <summary>
    /// Tolerant reader for Maxio date fields. The JSON REST responses use ISO 8601,
    /// but some payloads have been observed with "yyyy-MM-dd HH:mm:ss zzz" formatting,
    /// and empty strings can appear in nullable date fields.
    /// </summary>
    public class MaxioNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        private static readonly string[] KnownFormats =
        [
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.fffK",
            "yyyy-MM-dd HH:mm:ss zzz",
            "yyyy-MM-dd HH:mm:ssK",
            "yyyy-MM-dd",
        ];

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParseExact(value, KnownFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            {
                return exact;
            }

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            throw new JsonException($"Could not parse date/time value '{value}'.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Requests do not contain date/time fields.");
    }
}

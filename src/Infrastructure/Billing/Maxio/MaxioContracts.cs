using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Wire contracts mirroring maxio-spec/openapi.yaml (Advanced Billing REST API).
/// Property names are the snake_case names published by the spec; the envelopes
/// ("customer", "subscription", "product", ...) follow the same document.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        // Maxio mixes types: money arrives as a JSON number on products but as a quoted
        // decimal string on components ("unit_price": "0.01").
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Unexpected empty JSON payload for {typeof(T).Name}.");

    /// <summary>
    /// Maxio answers list calls with a JSON array of envelopes, but a single record call with the
    /// bare envelope - and some list endpoints answer with a bare envelope when there is exactly
    /// one record. This helper normalizes every shape into a list of the inner resource.
    /// </summary>
    public static IReadOnlyList<T> ReadResourceList<T>(string json, string envelopePropertyName)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var results = new List<T>();
        foreach (var item in Enumerate(root))
        {
            var resource = item.ValueKind == JsonValueKind.Object && item.TryGetProperty(envelopePropertyName, out var inner)
                ? inner
                : item;

            if (resource.ValueKind != JsonValueKind.Object)
                continue;

            var value = resource.Deserialize<T>(Options);
            if (value is not null)
                results.Add(value);
        }

        return results;
    }

    /// <summary>Reads a single resource, accepting both bare-envelope and one-element-array bodies.</summary>
    public static T? ReadResource<T>(string json, string envelopePropertyName)
    {
        var items = ReadResourceList<T>(json, envelopePropertyName);
        return items.Count > 0 ? items[0] : default;
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root };
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

internal sealed class ComponentEnvelope
{
    [JsonPropertyName("component")] public MaxioComponent? Component { get; set; }
}

internal sealed class ProductFamilyEnvelope
{
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>components/schemas/Customer.yaml</summary>
internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("organization")] public string? Organization { get; set; }
    [JsonPropertyName("created_at")] public MaxioDate CreatedAt { get; set; }
}

/// <summary>components/schemas/Product.yaml</summary>
internal sealed class MaxioProduct
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("archived_at")] public MaxioDate ArchivedAt { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>components/schemas/Product-Family.yaml</summary>
internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

/// <summary>components/schemas/Component.yaml (metered and friends share these fields)</summary>
internal sealed class MaxioComponent
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("unit_name")] public string? UnitName { get; set; }
    [JsonPropertyName("unit_price")] public decimal? UnitPrice { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("product_family_id")] public long ProductFamilyId { get; set; }
}

/// <summary>components/schemas/Subscription.yaml</summary>
internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("balance_in_cents")] public long? BalanceInCents { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    [JsonPropertyName("created_at")] public MaxioDate CreatedAt { get; set; }
    [JsonPropertyName("activated_at")] public MaxioDate ActivatedAt { get; set; }
    [JsonPropertyName("current_period_started_at")] public MaxioDate CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public MaxioDate CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public MaxioDate NextAssessmentAt { get; set; }
    [JsonPropertyName("expires_at")] public MaxioDate ExpiresAt { get; set; }
    [JsonPropertyName("canceled_at")] public MaxioDate CanceledAt { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

/// <summary>
/// POST /customers.json body: components/schemas/Create-Customer-Request.yaml (customer_attributes
/// subset - the fields eShopOnWeb actually owns).
/// </summary>
internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")] public CustomerAttributes Customer { get; set; } = new();

    internal sealed class CustomerAttributes
    {
        [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("organization")] public string? Organization { get; set; }
    }
}

/// <summary>
/// POST /subscriptions.json body: components/schemas/Create-Subscription-Request.yaml.
/// The subscription is linked to the existing customer by its reference and to the plan by product handle,
/// which is the pairing the spec recommends ("we recommend using the API Handle instead" of ids).
/// </summary>
internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")] public SubscriptionAttributes Subscription { get; set; } = new();

    internal sealed class SubscriptionAttributes
    {
        [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
        [JsonPropertyName("customer_reference")] public string CustomerReference { get; set; } = string.Empty;
        [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    }
}

/// <summary>Maxio error body: components/schemas/errors/* ({"errors": [...] } or {"errors": {...}}).</summary>
internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")] public JsonElement Errors { get; set; }

    public IReadOnlyList<string> AsMessages()
    {
        var messages = new List<string>();
        if (Errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in Errors.EnumerateArray())
                messages.Add(error.ValueKind == JsonValueKind.String ? error.GetString() ?? string.Empty : error.GetRawText());
        }
        else if (Errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in Errors.EnumerateObject())
                messages.Add($"{property.Name}: {(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText())}");
        }
        else if (Errors.ValueKind == JsonValueKind.String)
        {
            messages.Add(Errors.GetString() ?? string.Empty);
        }

        return messages;
    }
}

/// <summary>
/// Tolerant date reader: Maxio emits offset-aware ISO-8601 timestamps, plain ISO timestamps and
/// date-only values depending on the endpoint, and nulls for unset dates.
/// </summary>
internal readonly struct MaxioDate
{
    public MaxioDate(DateTimeOffset? value) => Value = value;

    public DateTimeOffset? Value { get; }

    public static implicit operator DateTimeOffset?(MaxioDate date) => date.Value;

    public static MaxioDate Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new MaxioDate(null);

        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeOffset.None, out var withOffset))
            return new MaxioDate(withOffset);

        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var local))
            return new MaxioDate(new DateTimeOffset(local));

        return new MaxioDate(null);
    }
}

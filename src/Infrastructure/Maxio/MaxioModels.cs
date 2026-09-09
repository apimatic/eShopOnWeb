using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wire models mirroring the maxio-spec/openapi.yaml response schemas. Property names map to the
/// spec's snake_case JSON via <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ArchivedAt { get; set; }
    public bool? RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Mirrors components/schemas/Product-Response.yaml: GET /products.json returns
/// [{"product": {...}}, ...].
/// </summary>
internal class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Mirrors components/schemas/Subscription-Response.yaml: subscription endpoints return
/// {"subscription": {...}} (or arrays of it).
/// </summary>
internal class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// Mirrors components/schemas/Customer-Response.yaml.
/// </summary>
internal class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
}

internal class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public long? PriceInCents { get; set; }
}

internal class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public JsonElement? Errors { get; set; }

    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }
}

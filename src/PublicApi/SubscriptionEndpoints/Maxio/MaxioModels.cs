using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

// Transport models for the Maxio Advanced Billing (Billing API) endpoints used
// by this integration. Property names follow the API's snake_case via the
// JsonNamingPolicy applied in MaxioBillingClient.

public sealed class MaxioProductFamilyRef
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    public int Id { get; set; }

    public string? Handle { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public bool? RequireCreditCard { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public DateTime? CreatedAt { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string? Reference { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? ProductPriceInCents { get; set; }

    public DateTime? CurrentPeriodEndsAt { get; set; }

    public DateTime? NextAssessmentAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public MaxioSubscriptionProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionProduct
{
    public int Id { get; set; }

    public string? Handle { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? PriceInCents { get; set; }
}

// Envelopes: the Billing API wraps single objects and returns arrays of
// wrapped objects (e.g. [ { "product": { ... } } ]).

internal sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal static class MaxioErrorParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Extracts the human-readable error list from a Billing API error payload.
    /// The "errors" member may be an array of strings or a keyed object.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new[] { "Unknown error from Maxio API." };
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("errors", out var errors) ||
                document.RootElement.TryGetProperty("error", out errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = new List<string>();
                    foreach (var item in errors.EnumerateArray())
                    {
                        messages.Add(item.ToString());
                    }
                    if (messages.Count > 0)
                    {
                        return messages;
                    }
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    foreach (var property in errors.EnumerateObject())
                    {
                        messages.Add($"{property.Name}: {property.Value}");
                    }
                    if (messages.Count > 0)
                    {
                        return messages;
                    }
                }
                else
                {
                    var text = errors.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new[] { text };
                    }
                }
            }
            return new[] { json };
        }
        catch (JsonException)
        {
            return new[] { json };
        }
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Wire shapes for the Maxio Billing API endpoints this integration calls. Property names map to the
// documented snake_case JSON via JsonNamingPolicy.SnakeCaseLower (see MaxioJson), so only fields the
// integration actually reads are declared here; anything else in a payload is ignored.

/// <summary>GET /site.json</summary>
internal sealed class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

internal sealed class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
}

/// <summary>GET /product_families/{id}/products.json returns a bare array of these.</summary>
internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }

    /// <summary>Whether a payment profile must be on file before the plan can be started.</summary>
    public bool RequireCreditCard { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>GET /customers/lookup.json and POST /customers.json</summary>
internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

internal sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// POST /subscriptions.json, GET /subscriptions/{id}.json and
/// GET /customers/{id}/subscriptions.json (a bare array of these).
/// </summary>
internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }

    /// <summary>The recurring amount this subscription is actually billed, in cents.</summary>
    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When payment will next be attempted; the shopper-facing "next billing date".</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Duplicate-prevention token. A repeat of the same token inside the server's window is rejected
    /// with 409 instead of creating a second subscription.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
}

/// <summary>
/// Error payloads come back either as {"errors": ["..."]} or as {"errors": {"field": "..."}};
/// <see cref="MaxioErrorReader"/> flattens both into a list of messages.
/// </summary>
internal static class MaxioErrorReader
{
    public static IReadOnlyList<string> Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            switch (errors.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    Add(messages, errors.GetString());
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        Add(messages, item.ValueKind == System.Text.Json.JsonValueKind.String
                            ? item.GetString()
                            : item.ToString());
                    }

                    break;
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var value = property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                        Add(messages, $"{property.Name}: {value}");
                    }

                    break;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // A non-JSON body (a gateway error page, say) carries nothing useful to surface.
            return Array.Empty<string>();
        }

        return messages;
    }

    private static void Add(ICollection<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message.Trim());
        }
    }
}

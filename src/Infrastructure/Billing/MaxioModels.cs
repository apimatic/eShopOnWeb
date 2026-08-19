using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class MaxioErrorResponse
{
    public JsonElement Errors { get; set; }
}

internal static class MaxioErrorFormatter
{
    public static string Format(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Maxio request failed.";
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, MaxioJson.Options);
            if (parsed is null)
            {
                return Truncate(body);
            }

            if (parsed.Errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in parsed.Errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString() ?? string.Empty);
                    }
                    else
                    {
                        parts.Add(item.ToString());
                    }
                }

                return parts.Count > 0 ? string.Join(" ", parts) : Truncate(body);
            }

            if (parsed.Errors.ValueKind == JsonValueKind.Object || parsed.Errors.ValueKind == JsonValueKind.String)
            {
                return parsed.Errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to raw body.
        }

        return Truncate(body);
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];
}

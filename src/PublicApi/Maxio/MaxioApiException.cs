using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio API responds with an unsuccessful status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public bool IsReferenceConflict => StatusCode == 422 && HasError("Reference");

    public IReadOnlyList<string> ErrorMessages()
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(ResponseBody))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(ResponseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in errors.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            messages.Add(item.GetString() ?? string.Empty);
                        }
                    }
                }
                else if (errors.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in errors.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in property.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    messages.Add($"{property.Name}: {item.GetString()}");
                                }
                            }
                        }
                        else if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            messages.Add($"{property.Name}: {property.Value.GetString()}");
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // fall through - expose the raw body below if nothing could be parsed
        }

        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(ResponseBody))
        {
            messages.Add(ResponseBody);
        }

        return messages;
    }

    private bool HasError(string fragment)
    {
        foreach (var error in ErrorMessages())
        {
            if (error.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Raised when an authenticated user asks to subscribe to a plan that does not
/// exist in the configured Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the caller sends an invalid subscription request.
/// </summary>
public class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message)
        : base(message)
    {
    }
}

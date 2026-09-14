using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// The message is derived from the error payload Maxio returns
/// (see the "errors" shapes in the OpenAPI specification).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public bool IsNotFound => StatusCode == (int)HttpStatusCode.NotFound;

    public static string FormatMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The Maxio Advanced Billing API returned an error.";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return FlattenErrors(errors);
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall back to the raw payload below.
        }

        return body.Trim();
    }

    private static string FlattenErrors(JsonElement errors)
    {
        var messages = new List<string>();
        Collect(errors, messages);
        return messages.Count > 0 ? string.Join(" ", messages) : errors.ToString();
    }

    private static void Collect(JsonElement node, List<string> messages)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(node.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    Collect(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    Collect(property.Value, messages);
                }
                break;
        }
    }
}

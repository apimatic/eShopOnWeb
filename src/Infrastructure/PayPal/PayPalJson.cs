using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Identifiers and JSON helpers shared by the PayPal client code.</summary>
internal static class PayPalHttpClient
{
    /// <summary>Name of the configured <see cref="System.Net.Http.HttpClient"/> for PayPal.</summary>
    public const string Name = "PayPal";
}

internal static class PayPalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Turns a PayPal error response body into a safe message and debug id for surfacing.</summary>
internal static class PayPalErrorReader
{
    public static (string Message, string? DebugId) Parse(string body)
    {
        try
        {
            var error = PayPalJson.Deserialize<PayPalErrorResponse>(body);
            if (error is null)
            {
                return ("PayPal returned an unrecognised error response.", null);
            }

            var message = error.Message
                ?? error.ErrorDescription
                ?? error.Name
                ?? error.Error
                ?? "PayPal returned an error.";

            var issue = error.Details?.FirstOrDefault()?.Issue;
            if (!string.IsNullOrEmpty(issue))
            {
                message = $"{message} ({issue})";
            }

            return (message, error.DebugId);
        }
        catch (JsonException)
        {
            return ("PayPal returned an error that could not be parsed.", null);
        }
    }
}

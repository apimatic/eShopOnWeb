using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class MaxioJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    internal static readonly MediaTypeHeaderValue JsonMediaType = new("application/json");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    internal static string BasicCredential(string apiKey)
        => Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
}

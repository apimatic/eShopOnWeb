using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP wrapper around the Maxio Billing API. Handles Basic authentication,
/// snake_case JSON serialization and error-body translation.
/// </summary>
public class MaxioClient
{
    public const string HttpClientName = "Maxio";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Sends a GET request. Returns null when the API responds 404 Not Found.
    /// </summary>
    public async Task<T?> GetAsync<T>(string path) where T : class
    {
        using var response = await _httpClient.GetAsync(path);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions);
    }

    public async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, SerializerOptions);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(SerializerOptions))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException(
            $"Maxio Billing API call {(response.RequestMessage?.Method)} {response.RequestMessage?.RequestUri} " +
            $"failed with {(int)response.StatusCode} {response.StatusCode}: {DescribeError(body)}",
            (int)response.StatusCode);
    }

    /// <summary>
    /// Maxio error bodies are either {"errors": ["..."]} or {"errors": {"field": ["..."]}}.
    /// </summary>
    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(no body)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return body;
            }

            var messages = new List<string>();
            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    messages.AddRange(errors.EnumerateArray().Select(e => e.ToString()));
                    break;
                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var values = property.Value.ValueKind == JsonValueKind.Array
                            ? string.Join("; ", property.Value.EnumerateArray().Select(e => e.ToString()))
                            : property.Value.ToString();
                        messages.Add($"{property.Name}: {values}");
                    }
                    break;
                default:
                    messages.Add(errors.ToString());
                    break;
            }

            return messages.Count > 0 ? string.Join(" | ", messages) : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}

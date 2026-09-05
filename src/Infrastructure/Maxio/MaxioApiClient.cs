using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin JSON/HTTP wrapper around the Maxio Advanced Billing API described by maxio-spec/openapi.yaml.
/// Base address and Basic-Auth credentials are configured on the injected <see cref="HttpClient"/>
/// by <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
public class MaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Returns null (rather than throwing) on a 404, so callers can treat "not found" as a normal outcome.</summary>
    public async Task<TResponse?> GetAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string requestUri, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(requestUri, body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, DescribeError(response.StatusCode, body));
    }

    private static string DescribeError(HttpStatusCode statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray().Select(DescribeErrorEntry),
                    JsonValueKind.Object => errors.EnumerateObject().Select(p => $"{p.Name}: {DescribeErrorEntry(p.Value)}"),
                    _ => new[] { errors.ToString() }
                };
                return string.Join("; ", messages);
            }
        }
        catch (JsonException)
        {
            // fall through to the raw-body message below
        }

        return $"Maxio API request failed with status {(int)statusCode}: {body}";
    }

    private static string DescribeErrorEntry(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : element.ToString();
}

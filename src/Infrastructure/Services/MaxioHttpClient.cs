using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioHttpClient> _logger;

    public MaxioHttpClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioHttpClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<T?> PostAsync<T>(string endpoint, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_config.GetApiBaseUrl()}{endpoint}";
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Maxio API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Maxio API returned {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio POST {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            var url = $"{_config.GetApiBaseUrl()}{endpoint}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Maxio API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Maxio API returned {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio GET {Endpoint}", endpoint);
            throw;
        }
    }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_config.GetBaseUrl());
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
    }

    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            _logger.LogInformation($"GET {endpoint}");
            var response = await _httpClient.GetAsync(endpoint);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio API error: {response.StatusCode} - {content}");
                return null;
            }

            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio GET {endpoint}");
            return null;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? body = null) where T : class
    {
        try
        {
            _logger.LogInformation($"POST {endpoint}");
            var jsonContent = body != null
                ? new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, "application/json")
                : new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, jsonContent);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio API error: {response.StatusCode} - {content}");
                return null;
            }

            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio POST {endpoint}");
            return null;
        }
    }
}

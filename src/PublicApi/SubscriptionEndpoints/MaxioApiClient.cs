using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioConfiguration> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.Subdomain))
        {
            throw new InvalidOperationException("Maxio API Key and Subdomain must be configured");
        }

        var baseUrl = _config.BaseUrl ?? $"https://{_config.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Maxio API GET {endpoint} returned {response.StatusCode}");
                return default;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio API GET {endpoint}");
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"Maxio API POST {endpoint} returned {response.StatusCode}: {errorContent}");
                throw new HttpRequestException($"Maxio API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio API POST {endpoint}");
            throw;
        }
    }
}

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, string apiKey, string baseUrl, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        try
        {
            var url = $"{_baseUrl}/{endpoint.TrimStart('/')}";
            _logger.LogInformation($"GET {url}");

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"GET {url} failed: {response.StatusCode} - {content}");
                return default;
            }

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return JsonSerializer.Deserialize<T>(content, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error in GetAsync: {ex.Message}");
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? payload = null)
    {
        try
        {
            var url = $"{_baseUrl}/{endpoint.TrimStart('/')}";
            _logger.LogInformation($"POST {url}");

            var jsonContent = payload != null
                ? new StringContent(
                    JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    Encoding.UTF8,
                    "application/json")
                : new StringContent(string.Empty, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, jsonContent);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"POST {url} failed: {response.StatusCode} - {content}");
                return default;
            }

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return JsonSerializer.Deserialize<T>(content, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error in PostAsync: {ex.Message}");
            throw;
        }
    }
}

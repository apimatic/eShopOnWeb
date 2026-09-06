using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioApiClient
{
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object data) where T : class;
    Task<T?> PutAsync<T>(string endpoint, object data) where T : class;
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("X-Reason-Code", "integration");
    }

    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/{endpoint.TrimStart('/')}";
            _logger.LogInformation("GET {Url}", url);

            var response = await _httpClient.GetAsync(url);
            return await HandleResponse<T>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GET request to {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object data) where T : class
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/{endpoint.TrimStart('/')}";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("POST {Url} with data: {Data}", url, json);

            var response = await _httpClient.PostAsync(url, content);
            return await HandleResponse<T>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in POST request to {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> PutAsync<T>(string endpoint, object data) where T : class
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/{endpoint.TrimStart('/')}";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("PUT {Url} with data: {Data}", url, json);

            var response = await _httpClient.PutAsync(url, content);
            return await HandleResponse<T>(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PUT request to {Endpoint}", endpoint);
            throw;
        }
    }

    private async Task<T?> HandleResponse<T>(HttpResponseMessage response) where T : class
    {
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("API Error: {StatusCode} - {Content}", response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }

        if (string.IsNullOrEmpty(content))
            return null;

        var result = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return result;
    }
}

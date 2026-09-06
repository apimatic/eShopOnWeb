using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiClient
{
    Task<T?> GetAsync<T>(string path);
    Task<T?> PostAsync<T>(string path, object? body = null);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MaxioApiClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var baseUrl = config.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            var encodedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encodedKey);
        }
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            _logger.LogInformation("GET {Path}", path);
            var response = await _httpClient.GetAsync(path);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GET {Path}", path);
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        try
        {
            _logger.LogInformation("POST {Path}", path);
            var json = body != null ? JsonSerializer.Serialize(body, JsonOptions) : string.Empty;
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(path, content);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Path}", path);
            throw;
        }
    }
}

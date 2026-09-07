using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioClient
{
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object? body) where T : class;
    Task<T?> ListAsync<T>(string endpoint) where T : class;
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = $"https://{_settings.Subdomain}.chargify.com";
        }
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.ApiKey}:X")
        );
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            _logger.LogInformation("GET {endpoint}", endpoint);
            var response = await _httpClient.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? body) where T : class
    {
        try
        {
            _logger.LogInformation("POST {endpoint}", endpoint);
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error posting to {endpoint}: {statusCode} - {response}",
                    endpoint, response.StatusCode, responseContent);
                throw new HttpRequestException($"Status: {response.StatusCode}, Content: {responseContent}");
            }

            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting to {endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> ListAsync<T>(string endpoint) where T : class
    {
        return await GetAsync<T>(endpoint);
    }
}

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

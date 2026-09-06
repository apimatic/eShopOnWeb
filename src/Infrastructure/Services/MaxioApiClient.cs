using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiClient
{
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object? body);
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
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var url = $"{_settings.GetBaseUrl()}{endpoint}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        SetAuthHeader(request);

        try
        {
            _logger.LogInformation("Calling Maxio API GET {Url}", url);
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Maxio API {Endpoint} returned {StatusCode}. Content length: {Length}", endpoint, response.StatusCode, content.Length);
            _logger.LogInformation("Response preview: {Content}", content.Substring(0, Math.Min(200, content.Length)));

            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio API GET {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? body)
    {
        var url = $"{_settings.GetBaseUrl()}{endpoint}";
        var json = body != null ? JsonSerializer.Serialize(body) : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetAuthHeader(request);

        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _logger.LogInformation("Maxio API POST {Url} with body: {Body}", url, json);
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Maxio API POST {Endpoint} returned {StatusCode}. Content: {Content}", endpoint, response.StatusCode, content.Substring(0, Math.Min(300, content.Length)));

            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio API POST {Endpoint}", endpoint);
            throw;
        }
    }

    private void SetAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }
}

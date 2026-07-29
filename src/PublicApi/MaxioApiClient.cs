using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

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
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        var url = $"{_config.GetBaseUrl()}{path}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request);
        await LogResponse(response, url, "GET");

        if (!response.IsSuccessStatusCode)
            return default;

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<T?> PostAsync<T>(string path, object? body)
    {
        var url = $"{_config.GetBaseUrl()}{path}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeader(request);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request);
        await LogResponse(response, url, "POST");

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio API error for {Path}: {StatusCode} - {Error}", path, response.StatusCode, errorContent);
            return default;
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ApiKey}:x"));
        request.Headers.Add("Authorization", $"Basic {credentials}");
    }

    private async Task LogResponse(HttpResponseMessage response, string url, string method)
    {
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Maxio API {Method} {Url}: {StatusCode}", method, url, response.StatusCode);
        }
        else
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Maxio API {Method} {Url}: {StatusCode} - {Content}", method, url, response.StatusCode, content);
        }
    }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioApiClient
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
        try
        {
            var uri = new Uri(new Uri(_settings.GetApiBaseUrl()), endpoint.TrimStart('/'));
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio API error: {response.StatusCode} - {content}");
                return default;
            }

            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio GET {endpoint}");
            throw;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object body)
    {
        try
        {
            var uri = new Uri(new Uri(_settings.GetApiBaseUrl()), endpoint.TrimStart('/'));
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio API error: {response.StatusCode} - {responseContent}");
                throw new InvalidOperationException($"Maxio API error: {response.StatusCode}");
            }

            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calling Maxio POST {endpoint}");
            throw;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:X"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }
}

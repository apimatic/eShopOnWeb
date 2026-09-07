using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioClientService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<MaxioClientService> _logger;

    public MaxioClientService(HttpClient httpClient, string apiKey, string baseUrl, ILogger<MaxioClientService> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string path) where T : class
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
            AddAuthHeader(request);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Maxio GET request failed: {Status} {Content}", response.StatusCode, content);
                return null;
            }

            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio GET {Path}", path);
            return null;
        }
    }

    public async Task<T?> PostAsync<T>(string path, object requestBody) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{path}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddAuthHeader(request);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Maxio POST request failed: {Status} {Content}", response.StatusCode, responseContent);
                return null;
            }

            return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio POST {Path}", path);
            return null;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    }
}

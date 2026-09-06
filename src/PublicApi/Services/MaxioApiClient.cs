using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioApiClient
{
    Task<T> GetAsync<T>(string endpoint);
    Task<T> PostAsync<T>(string endpoint, object data);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly string _authHeader;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
        _authHeader = $"Basic {auth}";

        _httpClient.DefaultRequestHeaders.Add("Authorization", _authHeader);
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl() + "/");
    }

    public async Task<T> GetAsync<T>(string endpoint)
    {
        try
        {
            _logger.LogInformation("GET {Endpoint}", endpoint);
            var response = await _httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Maxio API error {StatusCode}: {Content}", response.StatusCode, content);
                throw new HttpRequestException($"Maxio API returned {response.StatusCode}: {content}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            return JsonSerializer.Deserialize<T>(json, options) ?? throw new InvalidOperationException("Failed to deserialize response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio GET {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<T> PostAsync<T>(string endpoint, object data)
    {
        try
        {
            _logger.LogInformation("POST {Endpoint}", endpoint);
            var json = JsonSerializer.Serialize(data);
            _logger.LogDebug("Request body: {Body}", json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Maxio API error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new HttpRequestException($"Maxio API returned {response.StatusCode}: {responseContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Response: {Response}", responseJson);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            return JsonSerializer.Deserialize<T>(responseJson, options) ?? throw new InvalidOperationException("Failed to deserialize response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio POST {Endpoint}", endpoint);
            throw;
        }
    }
}

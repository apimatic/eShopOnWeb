using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:")));
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            return _config.BaseUrl.TrimEnd('/');
        }
        return $"https://{_config.Subdomain}.maxio.com/api".TrimEnd('/');
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        var url = $"{GetBaseUrl()}{path}";
        _logger.LogInformation("Maxio GET: {url}", url);

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio error {statusCode}: {content}", response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<T?> PostAsync<T>(string path, object body)
    {
        var url = $"{GetBaseUrl()}{path}";
        _logger.LogInformation("Maxio POST: {url}", url);

        var json = JsonSerializer.Serialize(body);
        _logger.LogDebug("Maxio request body: {json}", json);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio error {statusCode}: {content}", response.StatusCode, errorContent);
            response.EnsureSuccessStatusCode();
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

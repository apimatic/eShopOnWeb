using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioHttpClient> _logger;

    public MaxioHttpClient(HttpClient httpClient, IOptions<MaxioConfiguration> options, ILogger<MaxioHttpClient> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "eShopOnWeb");
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var url = $"{_config.GetBaseUrl()}{endpoint}";
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        SetAuthHeader(requestMessage);

        _logger.LogDebug("POST {Endpoint}", endpoint);
        var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("Response Status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Maxio response");
            return null;
        }
    }

    public async Task<TResponse?> GetAsync<TResponse>(
        string endpoint,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var url = $"{_config.GetBaseUrl()}{endpoint}";

        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        SetAuthHeader(requestMessage);

        _logger.LogDebug("GET {Endpoint}", endpoint);
        var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("Response Status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Maxio response");
            return null;
        }
    }

    private void SetAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}

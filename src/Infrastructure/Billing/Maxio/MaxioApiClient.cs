using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin transport over the Billing API: authentication, JSON conventions and error translation.
/// It knows nothing about plans or subscribers - that is the gateway's job.
/// </summary>
public sealed class MaxioApiClient
{
    /// <summary>The API is snake_cased throughout, so property names map by convention.</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioSettings> _settings;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public MaxioSettings Settings => _settings.Value;

    /// <summary>Reads a resource, returning null when the provider reports it does not exist.</summary>
    public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);

        return await ReadAsync<T>(response, cancellationToken);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
        where T : class
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);

        return await ReadAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "GET", path, new[] { "The response body was empty." });
    }

    /// <param name="safeToRetry">
    /// True only when the payload carries a duplicate-prevention token, which is what makes
    /// replaying the request harmless.
    /// </param>
    public async Task<T> PostAsync<T>(string path, object payload, bool safeToRetry, CancellationToken cancellationToken = default)
        where T : class
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, payload.GetType(), options: SerializerOptions)
        };
        request.Options.Set(MaxioRetryHandler.SafeToRetryOption, safeToRetry);

        using var response = await SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, HttpMethod.Post, path, cancellationToken);

        return await ReadAsync<T>(response, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, "POST", path, new[] { "The response body was empty." });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException("The billing provider could not be reached.", innerException: exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(
                $"The billing provider did not respond within {Settings.TimeoutSeconds}s.",
                innerException: exception);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);

        throw new MaxioApiException((int)response.StatusCode, method.Method, path, ParseErrors(body));
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Billing API reports failures as <c>{"errors": ["..."]}</c> and occasionally as
    /// <c>{"errors": {"field": "..."}}</c>. Anything else is passed through as a single message.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body!);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { Truncate(body!) };
            }

            var messages = new List<string>();

            switch (errors.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in errors.EnumerateArray())
                    {
                        var message = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            messages.Add(message!);
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var property in errors.EnumerateObject())
                    {
                        var message = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                        messages.Add($"{property.Name}: {message}");
                    }

                    break;

                case JsonValueKind.String:
                    var single = errors.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                    {
                        messages.Add(single!);
                    }

                    break;
            }

            return messages;
        }
        catch (JsonException)
        {
            return new[] { Truncate(body!) };
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value.Substring(0, 500) + "...";

    /// <summary>
    /// Fails fast, and specifically, when the billing credentials are missing. Public so callers
    /// can check before doing any work of their own that assumes a usable configuration.
    /// </summary>
    public void EnsureConfigured()
    {
        var settings = Settings;
        if (settings.IsConfigured)
        {
            return;
        }

        var detail = settings.IsAbsent
            ? $"The '{MaxioSettings.ConfigurationSectionName}' configuration section is missing."
            : string.Join(" ", settings.Problems());

        throw new BillingNotConfiguredException($"Subscription billing is not configured. {detail}");
    }
}

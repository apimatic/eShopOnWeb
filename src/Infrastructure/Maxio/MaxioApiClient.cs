using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Shared System.Text.Json options for Maxio payloads. Maxio uses snake_case throughout, so
/// the DTOs are declared in PascalCase and mapped via the naming policy.
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Result of a Maxio HTTP call: status code plus the raw response body, with helpers to
/// deserialize the body and to extract human-readable error messages.
/// </summary>
internal sealed class MaxioResult
{
    public MaxioResult(int statusCode, string body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    public int StatusCode { get; }
    public string Body { get; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public T? Deserialize<T>()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(Body, MaxioJson.Options);
    }

    /// <summary>
    /// Best-effort extraction of provider error messages. Maxio returns <c>errors</c> as either an
    /// array of strings or an object keyed by field; both shapes are handled here.
    /// </summary>
    public IReadOnlyList<string> ExtractErrors()
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(Body))
        {
            return messages;
        }

        try
        {
            using var doc = JsonDocument.Parse(Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errors))
            {
                CollectStrings(errors, messages);
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; nothing structured to extract.
        }

        return messages;
    }

    private static void CollectStrings(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    into.Add(s!);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, into);
                }
                break;
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    CollectStrings(prop.Value, into);
                }
                break;
        }
    }
}

/// <summary>
/// Thin typed HttpClient wrapper over the Maxio REST API. Handles JSON (de)serialization and
/// transient-fault retries (HTTP 429 and 5xx, plus network exceptions) with a short backoff,
/// in line with Maxio's guidance to pause and slow down rather than hammer the API.
/// </summary>
public sealed class MaxioApiClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient http, ILogger<MaxioApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    internal Task<MaxioResult> GetAsync(string relativePath, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, relativePath, body: null, cancellationToken);

    internal Task<MaxioResult> PostAsync(string relativePath, object body, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, relativePath, body, cancellationToken);

    private async Task<MaxioResult> SendAsync(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Maxio request {Method} {Path} failed (attempt {Attempt}/{Max}); retrying.", method, relativePath, attempt, MaxAttempts);
                await DelayForAttemptAsync(attempt, cancellationToken);
                continue;
            }

            var statusCode = (int)response.StatusCode;
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            if ((statusCode == 429 || statusCode >= 500) && attempt < MaxAttempts)
            {
                _logger.LogWarning("Maxio request {Method} {Path} returned {Status} (attempt {Attempt}/{Max}); retrying.", method, relativePath, statusCode, attempt, MaxAttempts);
                await DelayForAttemptAsync(attempt, cancellationToken);
                continue;
            }

            return new MaxioResult(statusCode, content);
        }
    }

    private static Task DelayForAttemptAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
}

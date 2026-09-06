using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin transport over the Maxio Billing API: JSON in, JSON out, upstream failures translated
/// into <see cref="BillingApiException"/>. Authentication is HTTP Basic with the API key as the
/// username and the literal "X" as the password, as the Billing API authentication docs specify.
/// Retries, backoff and concurrency limiting live in <see cref="MaxioResilienceHandler"/>.
/// </summary>
public class MaxioApiClient
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>Issues a GET, returning null when Maxio reports the resource does not exist.</summary>
    public Task<TResponse?> GetOrDefaultAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
        where TResponse : class =>
        SendAsync<TResponse>(new HttpRequestMessage(HttpMethod.Get, relativeUrl), allowNotFound: true, cancellationToken);

    /// <summary>
    /// Issues a POST. <paramref name="uniquenessToken"/> is Maxio's duplicate-prevention token:
    /// a replay of the same token within 60 minutes comes back as 409 rather than creating a
    /// second record, which is what makes retrying the request safe.
    /// </summary>
    public async Task<TResponse> PostAsync<TResponse>(string relativeUrl, object payload, string uniquenessToken,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(payload, payload.GetType(), options: SerializerOptions)
        };
        request.Options.Set(MaxioResilienceHandler.SafeToRetryKey, !string.IsNullOrEmpty(uniquenessToken));

        var response = await SendAsync<TResponse>(request, allowNotFound: false, cancellationToken);

        // allowNotFound is false, so a null here means Maxio returned a success status with no body.
        return response ?? throw new BillingApiException(
            $"Maxio returned an empty response body for POST {relativeUrl}.", (int)HttpStatusCode.BadGateway);
    }

    private async Task<TResponse?> SendAsync<TResponse>(HttpRequestMessage request, bool allowNotFound,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var stopwatch = Stopwatch.StartNew();

        using (request)
        {
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new BillingApiException(
                    $"Could not reach the Maxio Billing API for {request.Method} {request.RequestUri}.",
                    (int)HttpStatusCode.BadGateway, innerException: ex);
            }

            using (response)
            {
                _logger.LogInformation("Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                    request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds);

                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw await BuildApiExceptionAsync(request, response, cancellationToken);
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw new BillingApiException(
                        $"Could not parse the Maxio response for {request.Method} {request.RequestUri}.",
                        (int)HttpStatusCode.BadGateway, innerException: ex);
                }
            }
        }
    }

    private static async Task<BillingApiException> BuildApiExceptionAsync(HttpRequestMessage request,
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        var errors = ParseErrors(body);
        var summary = errors.Count > 0 ? string.Join("; ", errors) : Truncate(body);

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Maxio rejected the API credentials. Check the Maxio:ApiKey and Maxio:Subdomain settings.",
            HttpStatusCode.TooManyRequests =>
                "Maxio is throttling this site; the request was not processed.",
            HttpStatusCode.Conflict =>
                $"Maxio reported a duplicate submission for {request.Method} {request.RequestUri?.AbsolutePath}.",
            _ => $"Maxio returned {(int)response.StatusCode} for {request.Method} {request.RequestUri?.AbsolutePath}."
        };

        if (!string.IsNullOrWhiteSpace(summary))
        {
            message = $"{message} {summary}";
        }

        return new BillingApiException(message, (int)response.StatusCode, errors);
    }

    /// <summary>
    /// Maxio reports failures as { "errors": [ "..." ] } or, for validation failures,
    /// { "errors": { "field": "..." } } / { "errors": { "field": [ "..." ] } }.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return errors;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return errors;
            }

            CollectErrors(errorsElement, prefix: null, errors);
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page, for instance) - the raw body is used instead.
        }

        return errors;
    }

    private static void CollectErrors(JsonElement element, string? prefix, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    errors.Add(prefix is null ? text : $"{prefix}: {text}");
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, prefix, errors);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrors(property.Value, property.Name, errors);
                }

                break;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 400 ? value.Trim() : value[..400].Trim() + "...";
}

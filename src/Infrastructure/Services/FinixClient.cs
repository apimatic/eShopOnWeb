using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Hand-written Finix client. Every path, query parameter and payload below is taken from
/// finix-spec/finix-openapi.yaml. No Finix SDK is used.
/// </summary>
public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const int PageSize = 100;
    private const int MaxPages = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly FinixSettings _settings;
    private readonly ILogger<FinixClient> _logger;
    private readonly string _basicAuthValue;

    public FinixClient(HttpClient httpClient, FinixSettings settings, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var username = settings.Username ?? string.Empty;
        var password = settings.Password ?? string.Empty;
        _basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
    }

    public async Task<FinixIdentityResponse> CreateBuyerIdentityAsync(string? email, string? name,
        CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(name);
        var request = new FinixCreateIdentityRequest
        {
            Entity = new FinixIdentityEntity
            {
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        return await SendAsync<FinixIdentityResponse>(HttpMethod.Post, "/identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FinixPaymentInstrumentResponse>(HttpMethod.Post, "/payment_instruments", request, cancellationToken);

    public Task<FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixPaymentInstrumentResponse>(HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixUpdatePaymentInstrumentRequest { Enabled = false }, cancellationToken);

    public Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorizationResponse>(HttpMethod.Post, "/authorizations", request, cancellationToken);

    public Task<FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorizationResponse>(HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);

    public Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(string authorizationId,
        long captureAmountCents, CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorizationResponse>(HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents }, cancellationToken);

    public Task<FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorizationResponse>(HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest(), cancellationToken);

    public Task<FinixTransferResponse> GetTransferAsync(string transferId,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixTransferResponse>(HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);

    public Task<FinixTransferResponse> CreateRefundAsync(string captureTransferId, long refundAmountCents,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixTransferResponse>(HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(captureTransferId)}/reversals",
            new FinixCreateReversalRequest { RefundAmount = refundAmountCents }, cancellationToken);

    public async Task<IReadOnlyList<FinixTransferResponse>> ListTransferReversalsAsync(
        string captureTransferId, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransferResponse>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"/transfers/{Uri.EscapeDataString(captureTransferId)}/reversals?limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (response.Embedded?.Transfers != null)
            {
                results.AddRange(response.Embedded.Transfers);
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return results;
    }

    public Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default) =>
        ListAllAsync<FinixAuthorizationListResponse, FinixAuthorizationResponse>(
            "/authorizations", from, to,
            r => r.Embedded?.Authorizations, r => r.Page?.NextCursor, cancellationToken);

    public Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default) =>
        ListAllAsync<FinixTransferListResponse, FinixTransferResponse>(
            "/transfers", from, to,
            r => r.Embedded?.Transfers, r => r.Page?.NextCursor, cancellationToken);

    private async Task<IReadOnlyList<TItem>> ListAllAsync<TResponse, TItem>(string path,
        DateTimeOffset from, DateTimeOffset to,
        Func<TResponse, List<TItem>?> itemsSelector, Func<TResponse, string?> cursorSelector,
        CancellationToken cancellationToken)
    {
        var results = new List<TItem>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{path}?limit={PageSize}" +
                      $"&created_at.gte={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"))}" +
                      $"&created_at.lte={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss"))}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<TResponse>(HttpMethod.Get, url, null, cancellationToken);
            var items = itemsSelector(response);
            if (items != null)
            {
                results.AddRange(items);
            }

            cursor = cursorSelector(response);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        _settings.EnsureCredentials();
        var baseUrl = _settings.ResolveBaseUrl();

        using var request = new HttpRequestMessage(method, BuildUri(baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthValue);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(response.StatusCode, payload);
            _logger.LogWarning("Finix {Method} {Path} failed with {StatusCode}: {Message}",
                method.Method, path, (int)response.StatusCode, message);
            throw new FinixApiException((int)response.StatusCode, code, message);
        }

        _logger.LogInformation("Finix {Method} {Path} succeeded with {StatusCode}",
            method.Method, path, (int)response.StatusCode);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
               ?? throw new FinixApiException((int)response.StatusCode, "INVALID_RESPONSE",
                   "Finix returned an empty or invalid response.");
    }

    private static (string? Code, string Message) ParseError(HttpStatusCode statusCode, string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<FinixErrorResponse>(payload, JsonOptions);
            var first = error?.Embedded?.Errors is { Count: > 0 } errors ? errors[0] : null;
            if (first != null)
            {
                return (first.Code, first.Message ?? $"Finix request failed with HTTP {(int)statusCode}.");
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message; never echo an arbitrary body.
        }

        return (null, $"Finix request failed with HTTP {(int)statusCode} {statusCode}.");
    }

    private static (string? FirstName, string? LastName) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null);
        }

        var trimmed = name.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed, trimmed)
            : (trimmed[..space], trimmed[(space + 1)..]);
    }

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}

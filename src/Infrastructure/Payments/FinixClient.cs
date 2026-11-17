using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public Task<FinixIdentityResponse> CreateIdentityAsync(FinixCreateIdentityRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixIdentityResponse>(HttpMethod.Post, "/identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrumentResponse>(HttpMethod.Post, "/payment_instruments", request, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        var request = new FinixUpdatePaymentInstrumentRequest { Enabled = false };
        return SendAsync<FinixPaymentInstrumentResponse>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorizationResponse> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public Task<FinixAuthorizationResponse> CreateAuthorizationAsync(FinixCreateAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorizationResponse>(HttpMethod.Post, "/authorizations", request, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default)
    {
        var request = new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents };
        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorizationResponse> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var request = new FinixVoidAuthorizationRequest { VoidMe = true };
        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            request,
            cancellationToken);
    }

    public Task<FinixTransferResponse> CreateReversalAsync(string transferId, long refundAmountCents, CancellationToken cancellationToken = default)
    {
        var request = new FinixCreateReversalRequest { RefundAmount = refundAmountCents };
        return SendAsync<FinixTransferResponse>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(transferId)}/reversals",
            request,
            cancellationToken);
    }

    public Task<FinixTransferResponse> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransferResponse>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorizationResponse>();
        string? cursor = null;

        do
        {
            var path = BuildListPath("/authorizations", from, to, _options.MerchantId, cursor);
            var page = await SendAsync<FinixAuthorizationListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Authorizations is { } authorizations)
            {
                results.AddRange(authorizations);
            }

            cursor = page.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    public async Task<IReadOnlyList<FinixFeeResponse>> ListFeesAsync(string linkedTo, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixFeeResponse>();
        string? cursor = null;

        do
        {
            var path = $"/fees?linked_to={Uri.EscapeDataString(linkedTo)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await SendAsync<FinixFeeListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Fees is { } fees)
            {
                results.AddRange(fees);
            }

            cursor = page.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }
    public async Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransferResponse>();
        string? cursor = null;

        do
        {
            var path = BuildListPath("/transfers", from, to, _options.MerchantId, cursor);
            var page = await SendAsync<FinixTransferListResponse>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Transfers is { } transfers)
            {
                results.AddRange(transfers);
            }

            cursor = page.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private static string BuildListPath(string path, DateTimeOffset from, DateTimeOffset to, string merchantId, string? cursor)
    {
        var query = new StringBuilder();
        query.Append(path);
        query.Append("?created_at.gte=");
        query.Append(Uri.EscapeDataString(FormatDate(from)));
        query.Append("&created_at.lte=");
        query.Append(Uri.EscapeDataString(FormatDate(to)));
        query.Append("&merchant=");
        query.Append(Uri.EscapeDataString(merchantId));
        query.Append("&limit=100");

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Append("&after_cursor=");
            query.Append(Uri.EscapeDataString(cursor));
        }

        return query.ToString();
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        _options.Validate();
        var baseUrl = _options.ResolveBaseUrl();
        var uri = new Uri(baseUrl + "/" + path.TrimStart('/'));

        using var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            FinixErrorItem? error = null;
            try
            {
                var errorResponse = JsonSerializer.Deserialize<FinixErrorResponse>(content, JsonOptions);
                error = errorResponse?.Embedded?.Errors?.Count > 0 ? errorResponse.Embedded.Errors[0] : null;
            }
            catch (JsonException)
            {
            }

            var statusCode = (int)response.StatusCode;
            var message = error?.Message ?? $"Finix request failed with HTTP status {statusCode}.";
            _logger.LogWarning("Finix request {Method} {Path} failed with HTTP {StatusCode} ({ErrorCode})", method, path, statusCode, error?.Code);
            throw new FinixApiException(statusCode, message, error?.Code, error?.Logref);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)content;
        }

        var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException(500, "Finix returned an empty response.");
        }

        return result;
    }
}

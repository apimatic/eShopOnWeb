using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
    }

    public Task<FinixIdentity> CreateBuyerIdentityAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixIdentity>(
            HttpMethod.Post,
            "/identities",
            new FinixCreateIdentityRequest(),
            cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        FinixCreatePaymentInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Post,
            "/payment_instruments",
            request,
            cancellationToken);
    }

    public Task<FinixPaymentInstrument> UpdatePaymentInstrumentAsync(
        string paymentInstrumentId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixUpdatePaymentInstrumentRequest { Enabled = enabled },
            cancellationToken);
    }

    public Task<FinixAuthorization> CreateAuthorizationAsync(
        FinixCreateAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Post,
            "/authorizations",
            request,
            cancellationToken);
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/authorizations?limit=1&merchant={Uri.EscapeDataString(_options.MerchantId)}&idempotency_id={Uri.EscapeDataString(idempotencyId)}";
        var response = await SendAsync<FinixListResponse<FinixAuthorization>>(HttpMethod.Get, path, null, cancellationToken);
        return response.Items().FirstOrDefault();
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountInCents,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountInCents },
            cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest { VoidMe = true },
            cancellationToken);
    }

    public Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountInCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new FinixCreateRefundRequest
            {
                RefundAmount = refundAmountInCents,
                IdempotencyId = idempotencyId
            },
            cancellationToken);
    }

    public async Task<FinixTransfer?> FindTransferByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/transfers?limit=1&merchant={Uri.EscapeDataString(_options.MerchantId)}&idempotency_id={Uri.EscapeDataString(idempotencyId)}";
        var response = await SendAsync<FinixListResponse<FinixTransfer>>(HttpMethod.Get, path, null, cancellationToken);
        return response.Items().FirstOrDefault();
    }

    public Task<FinixTransfer> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var fees = new List<FinixFee>();
        string? cursor = null;

        do
        {
            var path = $"/fees?limit=100&linked_to={Uri.EscapeDataString(transferId)}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixListResponse<FinixFee>>(HttpMethod.Get, path, null, cancellationToken);
            fees.AddRange(response.Items());
            cursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return fees;
    }

    public Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return ListAllPagesAsync<FinixAuthorization>(
            "/authorizations",
            from,
            to,
            cancellationToken);
    }

    public Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return ListAllPagesAsync<FinixTransfer>(
            "/transfers",
            from,
            to,
            cancellationToken);
    }

    private async Task<IReadOnlyList<T>> ListAllPagesAsync<T>(
        string resourcePath,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        string? cursor = null;

        do
        {
            var path = $"{resourcePath}?limit=100" +
                       $"&merchant={Uri.EscapeDataString(_options.MerchantId)}" +
                       $"&created_at.gte={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}" +
                       $"&created_at.lte={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixListResponse<T>>(HttpMethod.Get, path, null, cancellationToken);
            items.AddRange(response.Items());
            cursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return items;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"{_options.ResolveBaseUrl()}{(path.StartsWith('/') ? path : "/" + path)}");
        using var request = new HttpRequestMessage(method, requestUri);
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException((int)response.StatusCode, responseBody);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)responseBody;
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result == null)
        {
            throw new FinixApiException((int)response.StatusCode, "Finix returned an empty response.");
        }

        return result;
    }

    private static FinixApiException CreateApiException(int statusCode, string responseBody)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseBody, JsonOptions);
            var error = envelope?.Embedded?.Errors?.FirstOrDefault();
            if (error != null)
            {
                return new FinixApiException(
                    statusCode,
                    error.Message ?? $"Finix request failed with HTTP {statusCode}.",
                    error.Code,
                    error.LogRef);
            }
        }
        catch (JsonException)
        {
        }

        return new FinixApiException(statusCode, $"Finix request failed with HTTP {statusCode}.");
    }
}

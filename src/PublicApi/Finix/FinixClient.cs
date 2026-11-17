using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public class FinixApiException : Exception
{
    public FinixApiException(HttpStatusCode statusCode, string? code, string? logRef, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }

    public bool IsIdempotencyConflict =>
        (Message?.Contains("idempotency", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Message?.Contains("idempotent", StringComparison.OrdinalIgnoreCase) ?? false);
}

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly FinixSettings _settings;

    public FinixClient(HttpClient http, IOptions<FinixSettings> settings)
    {
        _settings = settings.Value;
        _settings.EnsureConfigured();

        _http = http;
        _http.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("Finix-Version", FinixVersion);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.Username}:{_settings.Password}")));
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public Task<FinixIdentityResponse> CreateBuyerIdentityAsync(
        string email, string firstName, string lastName, FinixAddress address, CancellationToken cancellationToken)
    {
        var request = new CreateIdentityRequest
        {
            Entity = new CreateIdentityEntity
            {
                FirstName = string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
                LastName = string.IsNullOrWhiteSpace(lastName) ? "Shopper" : lastName,
                Email = email,
                PersonalAddress = address
            }
        };

        return SendAsync<FinixIdentityResponse>(HttpMethod.Post, "identities", request, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        string identityId, FinixCardInput card, CancellationToken cancellationToken)
    {
        var request = new CreatePaymentInstrumentRequest
        {
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Address = card.Address
        };

        return SendAsync<FinixPaymentInstrumentResponse>(
            HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResponse> DisablePaymentInstrumentAsync(
        string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var request = new UpdatePaymentInstrumentRequest { Enabled = false };
        return SendAsync<FinixPaymentInstrumentResponse>(
            HttpMethod.Put, $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", request, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> CreateAuthorizationAsync(
        long amountCents, string currency, string paymentInstrumentId, string idempotencyId, int orderId,
        CancellationToken cancellationToken)
    {
        var request = new CreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _settings.MerchantId!,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["source"] = "eshoponweb"
            }
        };

        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Post, "authorizations", request, cancellationToken);
    }

    public async Task<FinixAuthorizationResponse?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId, CancellationToken cancellationToken)
    {
        var url = $"authorizations?limit=1&idempotency_id={Uri.EscapeDataString(idempotencyId)}";
        var page = await GetAsync<FinixAuthorizationListResponse>(url, cancellationToken);
        return page.Embedded?.Authorizations.Count > 0 ? page.Embedded.Authorizations[0] : null;
    }

    public Task<FinixAuthorizationResponse> CaptureAuthorizationAsync(
        string authorizationId, long captureAmountCents, CancellationToken cancellationToken)
    {
        var request = new CaptureAuthorizationRequest { CaptureAmount = captureAmountCents };
        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put, $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixAuthorizationResponse> VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken)
    {
        var request = new VoidAuthorizationRequest { VoidMe = true };
        return SendAsync<FinixAuthorizationResponse>(
            HttpMethod.Put, $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public Task<FinixTransferResponse> GetTransferAsync(
        string transferId, CancellationToken cancellationToken)
    {
        return GetAsync<FinixTransferResponse>(
            $"transfers/{Uri.EscapeDataString(transferId)}", cancellationToken);
    }

    public Task<FinixTransferResponse> CreateRefundAsync(
        string captureTransferId, long amountCents, string idempotencyId, CancellationToken cancellationToken)
    {
        var request = new CreateRefundRequest
        {
            RefundAmount = amountCents,
            IdempotencyId = idempotencyId
        };

        return SendAsync<FinixTransferResponse>(
            HttpMethod.Post, $"transfers/{Uri.EscapeDataString(captureTransferId)}/reversals", request, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransferResponse>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<FinixTransferResponse>();
        string? cursor = null;

        for (var pageNumber = 0; pageNumber < 1000; pageNumber++)
        {
            var url = BuildListUrl("transfers", from, to, cursor);
            var page = await GetAsync<FinixTransferListResponse>(url, cancellationToken);
            if (page.Embedded?.Transfers is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Transfers);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<FinixAuthorizationResponse>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<FinixAuthorizationResponse>();
        string? cursor = null;

        for (var pageNumber = 0; pageNumber < 1000; pageNumber++)
        {
            var url = BuildListUrl("authorizations", from, to, cursor);
            var page = await GetAsync<FinixAuthorizationListResponse>(url, cancellationToken);
            if (page.Embedded?.Authorizations is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Authorizations);
            }

            cursor = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return results;
    }

    private static string BuildListUrl(string resource, DateTimeOffset from, DateTimeOffset to, string? cursor)
    {
        var fromValue = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");
        var toValue = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");
        var url = $"{resource}?limit=100&created_at.gte={Uri.EscapeDataString(fromValue)}&created_at.lte={Uri.EscapeDataString(toValue)}";

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&after_cursor={Uri.EscapeDataString(cursor)}";
        }

        return url;
    }

    private Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        return SendAsync<T>(HttpMethod.Get, relativeUrl, null, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException(
                response.StatusCode, "EMPTY_RESPONSE", null,
                "Finix returned an empty response.");
        }

        return result;
    }

    private static FinixApiException ToException(HttpStatusCode statusCode, string responseBody)
    {
        string? code = null;
        string? logRef = null;
        var message = $"Finix request failed with HTTP {(int)statusCode}.";

        try
        {
            var error = JsonSerializer.Deserialize<FinixErrorResponse>(responseBody, JsonOptions);
            var first = error?.Embedded?.Errors.Count > 0 ? error.Embedded.Errors[0] : null;
            if (first is not null)
            {
                code = first.Code;
                logRef = first.LogRef;
                if (!string.IsNullOrWhiteSpace(first.Message))
                {
                    message = first.Message!;
                }
            }
        }
        catch (JsonException)
        {
            // Do not include arbitrary upstream response bodies in errors; they may contain sensitive data.
        }

        return new FinixApiException(statusCode, code, logRef, message);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public class FinixClient
{
    private const string FinixVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options)
    {
        var finix = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(finix.ResolveBaseUrl().TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{finix.Username}:{finix.Password}")));
        _httpClient.DefaultRequestHeaders.Add("Finix-Version", FinixVersion);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<FinixIdentity> CreateIdentityAsync(CancellationToken cancellationToken)
    {
        var request = new FinixCreateIdentityRequest
        {
            Entity = new Dictionary<string, object>()
        };

        return await SendAsync<FinixIdentity>(HttpMethod.Post, "identities", request, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId,
        string name, string number, int expirationMonth, int expirationYear, string? securityCode,
        FinixAddressRequest? address, CancellationToken cancellationToken)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Identity = identityId,
            Name = name,
            Number = number,
            ExpirationMonth = expirationMonth,
            ExpirationYear = expirationYear,
            SecurityCode = securityCode,
            Address = address
        };

        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var request = new FinixUpdatePaymentInstrumentRequest { Enabled = false };
        await SendAsync<FinixPaymentInstrument>(HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", request, cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(long amountCents, string currency,
        string merchantId, string paymentInstrumentId, string idempotencyId, int orderId,
        CancellationToken cancellationToken)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = merchantId,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture)
            }
        };

        return await SendAsync<FinixAuthorization>(HttpMethod.Post, "authorizations", request, cancellationToken);
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return await SendAsync<FinixAuthorization>(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(string idempotencyId,
        CancellationToken cancellationToken)
    {
        var list = await SendAsync<FinixAuthorizationList>(HttpMethod.Get,
            $"authorizations?idempotency_id={Uri.EscapeDataString(idempotencyId)}&limit=100", null, cancellationToken);

        return list.Embedded?.Authorizations?.FirstOrDefault(a =>
            string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents,
        CancellationToken cancellationToken)
    {
        var request = new FinixCaptureAuthorizationRequest { CaptureAmount = captureAmountCents };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var request = new FinixVoidAuthorizationRequest { VoidMe = true };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", request, cancellationToken);
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        return await SendAsync<FinixTransfer>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public async Task<FinixTransfer> CreateRefundAsync(string transferId, long refundAmountCents,
        CancellationToken cancellationToken)
    {
        var request = new FinixCreateRefundRequest { RefundAmount = refundAmountCents };
        return await SendAsync<FinixTransfer>(HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals", request, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesForTransferAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var fees = new List<FinixFee>();
        string? cursor = null;

        do
        {
            var query = $"fees?linked_to={Uri.EscapeDataString(transferId)}&limit=100";
            if (!string.IsNullOrEmpty(cursor))
            {
                query += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixFeeList>(HttpMethod.Get, query, null, cancellationToken);
            if (response.Embedded?.Fees is { Count: > 0 })
            {
                fees.AddRange(response.Embedded.Fees);
            }

            cursor = response.Page?.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return fees;
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(string merchantId, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transfers = new List<FinixTransfer>();
        string? cursor = null;
        var fromValue = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toValue = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        do
        {
            var query = $"transfers?merchant={Uri.EscapeDataString(merchantId)}" +
                        $"&created_at.gte={Uri.EscapeDataString(fromValue)}" +
                        $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit=100";
            if (!string.IsNullOrEmpty(cursor))
            {
                query += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixTransferList>(HttpMethod.Get, query, null, cancellationToken);
            if (response.Embedded?.Transfers is { Count: > 0 })
            {
                transfers.AddRange(response.Embedded.Transfers);
            }

            cursor = response.Page?.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return transfers;
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(string merchantId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var authorizations = new List<FinixAuthorization>();
        string? cursor = null;
        var fromValue = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toValue = to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        do
        {
            var query = $"authorizations?merchant={Uri.EscapeDataString(merchantId)}" +
                        $"&created_at.gte={Uri.EscapeDataString(fromValue)}" +
                        $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit=100";
            if (!string.IsNullOrEmpty(cursor))
            {
                query += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await SendAsync<FinixAuthorizationList>(HttpMethod.Get, query, null, cancellationToken);
            if (response.Embedded?.Authorizations is { Count: > 0 })
            {
                authorizations.AddRange(response.Embedded.Authorizations);
            }

            cursor = response.Page?.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return authorizations;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateException((int)response.StatusCode, raw);
        }

        var result = JsonSerializer.Deserialize<T>(raw, JsonOptions);
        if (result is null)
        {
            throw new FinixApiException((int)response.StatusCode, null,
                "Finix returned an empty or invalid response.", raw);
        }

        return result;
    }

    private static FinixApiException CreateException(int statusCode, string raw)
    {
        string? code = null;
        string? message = null;

        try
        {
            var error = JsonSerializer.Deserialize<FinixErrorResponse>(raw, JsonOptions);
            var first = error?.Embedded?.Errors?.FirstOrDefault();
            code = first?.Code;
            message = first?.Message;
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(raw)
                ? $"Finix request failed with HTTP {statusCode}."
                : $"Finix request failed with HTTP {statusCode}: {Truncate(raw, 1000)}";
        }

        return new FinixApiException(statusCode, code, message, raw);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

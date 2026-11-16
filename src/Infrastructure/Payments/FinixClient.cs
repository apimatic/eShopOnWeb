using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixClient
{
    private const string FinixVersion = "2022-02-01";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient http, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _options.EnsureConfigured();
        _http.BaseAddress = new Uri(_options.ResolveBaseUrl().TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    internal Task<FinixIdentity> CreateIdentityAsync(CancellationToken cancellationToken)
    {
        return SendAsync<FinixIdentity>(HttpMethod.Post, "identities", new FinixCreateIdentityRequest(), cancellationToken);
    }

    internal Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, string number,
        int expirationMonth, int expirationYear, string? securityCode, string name, FinixAddressRequest? address,
        CancellationToken cancellationToken)
    {
        var request = new FinixCreatePaymentInstrumentRequest
        {
            Identity = identityId,
            Number = DigitsOnly(number),
            ExpirationMonth = expirationMonth,
            ExpirationYear = expirationYear,
            SecurityCode = securityCode,
            Name = name,
            Address = address
        };

        return SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "payment_instruments", request, cancellationToken);
    }

    internal Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        return SendAsync<FinixPaymentInstrument>(HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new FinixDisablePaymentInstrumentRequest(), cancellationToken);
    }

    internal Task<FinixAuthorization> CreateAuthorizationAsync(long amountCents, string currency, string source,
        string? idempotencyId, int orderId, CancellationToken cancellationToken)
    {
        var request = new FinixCreateAuthorizationRequest
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _options.MerchantId,
            Source = source,
            IdempotencyId = idempotencyId,
            Tags = new Dictionary<string, string> { ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture) }
        };

        return SendAsync<FinixAuthorization>(HttpMethod.Post, "authorizations", request, cancellationToken);
    }

    internal Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, long amountCents,
        CancellationToken cancellationToken)
    {
        return SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixCaptureAuthorizationRequest { CaptureAmount = amountCents }, cancellationToken);
    }

    internal Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new FinixVoidAuthorizationRequest(), cancellationToken);
    }

    internal Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return SendAsync<FinixAuthorization>(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    internal async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(string idempotencyId,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<FinixAuthorizationList>(HttpMethod.Get,
            $"authorizations?idempotency_id={Uri.EscapeDataString(idempotencyId)}&limit=1", null, cancellationToken);
        return page.Embedded?.Authorizations?.FirstOrDefault();
    }

    internal Task<FinixTransfer> CreateReversalAsync(string transferId, long amountCents,
        CancellationToken cancellationToken)
    {
        return SendAsync<FinixTransfer>(HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new FinixCreateReversalRequest { RefundAmount = amountCents }, cancellationToken);
    }

    internal Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        return SendAsync<FinixTransfer>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    internal async Task<decimal> GetFeesForTransferAsync(string transferId, TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + maxWait;
        while (true)
        {
            var fees = await ListFeesAsync(transferId, cancellationToken);
            if (fees.Count > 0)
            {
                return fees.Sum(f => f.Amount);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return 0m;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    internal async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        string? previousCursor = null;

        do
        {
            var query = new StringBuilder("transfers?");
            query.Append("created_at.gte=").Append(Uri.EscapeDataString(FormatFinixDateTime(from)));
            query.Append("&created_at.lte=").Append(Uri.EscapeDataString(FormatFinixDateTime(to)));
            query.Append("&limit=100");
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor));
            }

            var page = await SendAsync<FinixTransferList>(HttpMethod.Get, query.ToString(), null, cancellationToken);
            if (page.Embedded?.Transfers is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Transfers);
            }

            previousCursor = cursor;
            cursor = page.Page?.NextCursor;
            if (!string.IsNullOrWhiteSpace(cursor) && cursor == previousCursor)
            {
                break;
            }
        } while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<List<FinixFee>> ListFeesAsync(string transferId, CancellationToken cancellationToken)
    {
        var results = new List<FinixFee>();
        string? cursor = null;
        string? previousCursor = null;

        do
        {
            var query = new StringBuilder("fees?");
            query.Append("linked_to=").Append(Uri.EscapeDataString(transferId));
            query.Append("&limit=100");
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor));
            }

            var page = await SendAsync<FinixFeeList>(HttpMethod.Get, query.ToString(), null, cancellationToken);
            if (page.Embedded?.Fees is { Count: > 0 })
            {
                results.AddRange(page.Embedded.Fees);
            }

            previousCursor = cursor;
            cursor = page.Page?.NextCursor;
            if (!string.IsNullOrWhiteSpace(cursor) && cursor == previousCursor)
            {
                break;
            }
        } while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}")));
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message, logref) = ParseError(payload, response.ReasonPhrase);
            _logger.LogWarning("Finix {Method} {Path} failed with {Status} ({Code})",
                method.Method, path, (int)response.StatusCode, code);
            throw new FinixApiException(response.StatusCode, message, code, logref);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new FinixApiException(response.StatusCode, "Finix returned an empty response.");
        }
        catch (JsonException)
        {
            throw new FinixApiException(response.StatusCode, "Finix returned an unreadable response.");
        }
    }

    private static (string? Code, string Message, string? Logref) ParseError(string payload, string? fallback)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(payload, JsonOptions);
            var error = envelope?.Embedded?.Errors?.FirstOrDefault();
            if (error is not null)
            {
                return (error.Code, error.Message ?? fallback ?? "Finix request failed.", error.Logref);
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message; never echo request data here.
        }

        return (null, fallback ?? "Finix request failed.", null);
    }

    private static string FormatFinixDateTime(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string DigitsOnly(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }
}

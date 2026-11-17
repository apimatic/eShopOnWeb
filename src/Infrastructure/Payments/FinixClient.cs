using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Plain HTTP client for the Finix API. The contract used here was verified
/// against Finix's published API reference:
///   POST /identities, POST /payment_instruments, PUT /payment_instruments/{id}
///   POST /authorizations, GET /authorizations/{id}, PUT /authorizations/{id}
///   GET /transfers, GET /transfers/{id}, POST /transfers/{id}/reversals
///   GET /fees
/// Basic authentication and the Finix-Version: 2022-02-01 header are used on
/// every request.
/// </summary>
public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const int PageSize = 100;
    private const int MaxPages = 1000;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient httpClient, FinixOptions options, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new PaymentOperationException(500,
                "Finix credentials are not configured. Set Finix:Username and Finix:Password (from FINIX_USERNAME and FINIX_PASSWORD).");
        }

        _httpClient.BaseAddress = new Uri(options.ResolveBaseUrl().TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.Add("Finix-Version", FinixVersion);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<FinixIdentity> CreateBuyerIdentityAsync(string firstName, string lastName, string? email, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Type = "PERSONAL",
            IdentityRoles = new[] { "BUYER" },
            Entity = new
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                PersonalAddress = new
                {
                    Line1 = "1 eShop Way",
                    City = "Seattle",
                    Region = "WA",
                    PostalCode = "98101",
                    Country = "USA"
                }
            },
            Tags = new Dictionary<string, string> { ["app"] = "eshoponweb" }
        };

        return await SendAsync<FinixIdentity>(HttpMethod.Post, "identities", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(string identityId, FinixCardDetails card, FinixAddress address, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Type = "PAYMENT_CARD",
            Identity = identityId,
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            Address = address,
            Tags = new Dictionary<string, string> { ["app"] = "eshoponweb" }
        };

        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Post, "payment_instruments", body, cancellationToken);
    }

    public async Task<FinixPaymentInstrument> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        var body = new { Enabled = false };
        return await SendAsync<FinixPaymentInstrument>(HttpMethod.Put, $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}", body, cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(int amountCents, string currency, string source, string idempotencyId, IReadOnlyDictionary<string, string>? tags = null, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = RequireMerchantId(),
            Source = source,
            IdempotencyId = idempotencyId,
            Tags = tags
        };

        return await SendAsync<FinixAuthorization>(HttpMethod.Post, "authorizations", body, cancellationToken);
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixAuthorization>(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(string authorizationId, int captureAmountCents, CancellationToken cancellationToken = default)
    {
        var body = new { CaptureAmount = captureAmountCents };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", body, cancellationToken);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var body = new { VoidMe = true };
        return await SendAsync<FinixAuthorization>(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", body, cancellationToken);
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        return await SendAsync<FinixTransfer>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
    }

    public async Task<FinixTransfer> CreateRefundAsync(string parentTransferId, int refundAmountCents, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            RefundAmount = refundAmountCents,
            IdempotencyId = idempotencyKey
        };

        return await SendAsync<FinixTransfer>(HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(parentTransferId)}/reversals", body, cancellationToken);
    }

    public async Task<FinixTransfer?> FindTransferByIdempotencyAsync(string idempotencyId, CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        do
        {
            var path = $"transfers?merchant={Uri.EscapeDataString(RequireMerchantId())}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await SendAsync<FinixTransferList>(HttpMethod.Get, path, null, cancellationToken);
            var match = page.Embedded?.Transfers?
                .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyId, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor) && pages < MaxPages);

        return null;
    }

    public async Task<int?> GetUnreversedAmountAsync(string parentTransferId, CancellationToken cancellationToken = default)
    {
        var page = await SendAsync<FinixTransferList>(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(parentTransferId)}/reversals?limit=1", null, cancellationToken);
        return page.UnreversedAmount;
    }

    public async Task<IReadOnlyList<FinixFee>> ListFeesAsync(string linkedToId, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixFee>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        do
        {
            var path = $"fees?linked_to={Uri.EscapeDataString(linkedToId)}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await SendAsync<FinixFeeList>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Fees != null)
            {
                results.AddRange(page.Embedded.Fees);
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor) && pages < MaxPages);

        return results;
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        do
        {
            var path = $"transfers?merchant={Uri.EscapeDataString(RequireMerchantId())}" +
                       $"&created_at.gte={Uri.EscapeDataString(FormatDate(from))}" +
                       $"&created_at.lte={Uri.EscapeDataString(FormatDate(to))}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await SendAsync<FinixTransferList>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Transfers != null)
            {
                results.AddRange(page.Embedded.Transfers);
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor) && pages < MaxPages);

        return results;
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorization>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;

        do
        {
            var path = $"authorizations?merchant={Uri.EscapeDataString(RequireMerchantId())}" +
                       $"&created_at.gte={Uri.EscapeDataString(FormatDate(from))}" +
                       $"&created_at.lte={Uri.EscapeDataString(FormatDate(to))}&limit={PageSize}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            var page = await SendAsync<FinixAuthorizationList>(HttpMethod.Get, path, null, cancellationToken);
            if (page.Embedded?.Authorizations != null)
            {
                results.AddRange(page.Embedded.Authorizations);
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor) && pages < MaxPages);

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, WriteOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errors = ParseErrors(responseBody);
            _logger.LogWarning("Finix {Method} {Path} returned {StatusCode}: {Errors}",
                method.Method, relativePath.Split('?')[0], (int)response.StatusCode,
                string.Join("; ", errors.Select(e => e.Message)));
            throw new FinixApiException((int)response.StatusCode, errors, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new FinixApiException(502, new[] { new FinixError { Message = "Finix returned an empty response body." } });
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(responseBody, ReadOptions);
            if (result == null)
            {
                throw new FinixApiException(502, new[] { new FinixError { Message = "Finix returned an empty JSON object." } });
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new FinixApiException(502, new[]
            {
                new FinixError { Message = $"Could not read the Finix response: {ex.Message}" }
            });
        }
    }

    private static IReadOnlyList<FinixError> ParseErrors(string responseBody)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseBody, ReadOptions);
            if (envelope?.Embedded?.Errors is { Count: > 0 } errors)
            {
                return errors;
            }
        }
        catch (JsonException)
        {
        }

        return new[]
        {
            new FinixError
            {
                Message = string.IsNullOrWhiteSpace(responseBody)
                    ? "Finix returned an error without a body."
                    : responseBody
            }
        };
    }

    private string RequireMerchantId()
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new PaymentOperationException(500,
                "Finix merchant id is not configured. Set Finix:MerchantId (from FINIX_MERCHANT_ID).");
        }

        return _options.MerchantId;
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}

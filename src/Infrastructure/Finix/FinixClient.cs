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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const int PageLimit = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;
    private readonly SemaphoreSlim _merchantLock = new(1, 1);
    private string? _merchantIdentity;

    public FinixClient(HttpClient http, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetMerchantIdentityAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_merchantIdentity))
        {
            return _merchantIdentity!;
        }

        await _merchantLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_merchantIdentity))
            {
                return _merchantIdentity!;
            }

            var merchantId = _options.RequireMerchantId();
            var merchant = await SendAsync<FinixMerchant>(
                HttpMethod.Get,
                $"merchants/{Uri.EscapeDataString(merchantId)}",
                null,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(merchant.Identity))
            {
                throw new PaymentGatewayException(
                    $"Finix merchant '{merchantId}' has no associated identity.");
            }

            _merchantIdentity = merchant.Identity;
            return _merchantIdentity;
        }
        finally
        {
            _merchantLock.Release();
        }
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string identity,
        CardDetailsInput card,
        CancellationToken cancellationToken = default)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentValidationException("Card number is required.");
        }

        if (card.ExpirationMonth is null or < 1 or > 12 || card.ExpirationYear is null)
        {
            throw new PaymentValidationException("Card expiration month and year are required.");
        }

        var address = card.Address ?? new AddressInput();
        var request = new
        {
            type = "PAYMENT_CARD",
            number = NormalizeCardNumber(card.Number),
            expiration_month = card.ExpirationMonth.Value,
            expiration_year = card.ExpirationYear.Value,
            security_code = card.SecurityCode,
            name = string.IsNullOrWhiteSpace(card.Name) ? "eShopOnWeb Shopper" : card.Name,
            identity,
            address = new
            {
                line1 = string.IsNullOrWhiteSpace(address.Line1) ? "123 Main St." : address.Line1,
                line2 = address.Line2,
                city = string.IsNullOrWhiteSpace(address.City) ? "Kent" : address.City,
                region = string.IsNullOrWhiteSpace(address.Region) ? "OH" : address.Region,
                postal_code = string.IsNullOrWhiteSpace(address.PostalCode) ? "44240" : address.PostalCode,
                country = NormalizeCountry(address.Country)
            }
        };

        return await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Post,
            "payment_instruments",
            request,
            cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentInstrumentId))
        {
            return;
        }

        await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new { enabled = false },
            cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            amount = amountCents,
            currency,
            merchant = _options.RequireMerchantId(),
            source,
            idempotency_id = idempotencyId,
            tags
        };

        return await SendAsync<FinixAuthorization>(
            HttpMethod.Post,
            "authorizations",
            request,
            cancellationToken);
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { capture_amount = captureAmountCents },
            cancellationToken);

    public Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { void_me = true },
            cancellationToken);

    public Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default) =>
        SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);

    public Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        CancellationToken cancellationToken = default) =>
        SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new { refund_amount = refundAmountCents },
            cancellationToken);

    public Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        ListAllAsync<FinixTransferList, FinixTransfer>(
            "transfers",
            page => page.Embedded?.Transfers,
            page => page.Page?.NextCursor,
            from,
            to,
            cancellationToken);

    public Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        ListAllAsync<FinixAuthorizationList, FinixAuthorization>(
            "authorizations",
            page => page.Embedded?.Authorizations,
            page => page.Page?.NextCursor,
            from,
            to,
            cancellationToken);

    private async Task<IReadOnlyList<TItem>> ListAllAsync<TList, TItem>(
        string resource,
        Func<TList, IReadOnlyList<TItem>?> itemsSelector,
        Func<TList, string?> cursorSelector,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
        where TList : class
    {
        var results = new List<TItem>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        do
        {
            var path = new StringBuilder(resource);
            path.Append("?limit=").Append(PageLimit);
            path.Append("&merchant=").Append(Uri.EscapeDataString(_options.RequireMerchantId()));
            path.Append("&created_at.gte=").Append(Uri.EscapeDataString(FormatDate(from)));
            path.Append("&created_at.lte=").Append(Uri.EscapeDataString(FormatDate(to)));

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor!));
            }

            var page = await SendAsync<TList>(HttpMethod.Get, path.ToString(), null, cancellationToken);
            var items = itemsSelector(page);
            if (items is not null)
            {
                results.AddRange(items);
            }

            cursor = cursorSelector(page);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }

            if (!seenCursors.Add(cursor!))
            {
                _logger.LogWarning("Finix returned a repeated pagination cursor for {Resource}; stopping.", resource);
                break;
            }
        }
        while (true);

        return results;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        _options.ValidateCredentials();

        var baseUrl = _options.ResolveBaseUrl();
        using var request = new HttpRequestMessage(method, BuildUri(baseUrl, path));
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicToken());

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (message, code, relatedResourceId) = ParseError(responseText, response.StatusCode);
            _logger.LogWarning(
                "Finix request {Method} {Path} failed with {StatusCode} ({Code}): {Message}",
                method,
                path,
                (int)response.StatusCode,
                code,
                message);
            throw new PaymentGatewayException(message, code, relatedResourceId);
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return default!;
        }

        var result = JsonSerializer.Deserialize<T>(responseText, JsonOptions);
        if (result is null)
        {
            throw new PaymentGatewayException("Finix returned an empty response body.");
        }

        return result;
    }

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);

    private string BuildBasicToken()
    {
        var raw = $"{_options.Username}:{_options.Password}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (string Message, string? Code, string? RelatedResourceId) ParseError(
        string responseText,
        System.Net.HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<FinixErrorEnvelope>(responseText, JsonOptions);
                var errors = envelope?.Embedded?.Errors;
                if (errors is { Count: > 0 })
                {
                    var message = string.Join("; ", errors
                        .Select(e => e.Message)
                        .Where(m => !string.IsNullOrWhiteSpace(m)));
                    var code = errors.Select(e => e.Code).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                    var relatedId = errors
                        .Select(e => e.Authorization
                            ?? e.Transfer
                            ?? IdFromHref(e.Links?.Authorization?.Href)
                            ?? IdFromHref(e.Links?.Transfer?.Href))
                        .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return (message, code, relatedId);
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to the generic message below.
            }
        }

        return ($"Finix request failed with HTTP {(int)statusCode} ({statusCode}).", null, null);
    }

    private static string? IdFromHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var trimmed = href.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index >= 0 && index < trimmed.Length - 1
            ? trimmed[(index + 1)..]
            : null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static string NormalizeCountry(string? country)
    {
        var value = (country ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "" => "USA",
            "US" => "USA",
            "UNITED STATES" => "USA",
            "UNITED STATES OF AMERICA" => "USA",
            "CA" => "CAN",
            "CANADA" => "CAN",
            _ => value
        };
    }
}

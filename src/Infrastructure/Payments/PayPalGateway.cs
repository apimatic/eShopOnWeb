using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalGateway(HttpClient http, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new CheckoutException(500, "PayPal:Currency is not configured.");
            }

            return _options.Currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var source = new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };

        return AuthorizeAsync(amount, currency, invoiceId, lines, source, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var source = new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest { VaultId = vaultId }
        };

        return AuthorizeAsync(amount, currency, invoiceId, lines, source, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            idempotencyKey: null,
            cancellationToken);

        return MapAuthorizationDetails(dto);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest
        {
            Amount = Money(amount, currency)
        };

        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            request,
            idempotencyKey,
            cancellationToken);

        var details = MapAuthorizationDetails(dto);
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = details.AuthorizationId,
            Status = details.Status,
            CreateTime = details.CreateTime,
            ExpirationTime = details.ExpirationTime,
            Amount = ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true,
            InvoiceId = invoiceId
        };

        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            request,
            idempotencyKey,
            cancellationToken);

        var captured = ParseMoney(dto.Amount?.Value);
        var fee = ParseMoney(dto.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = ParseMoney(dto.SellerReceivableBreakdown?.NetAmount?.Value);
        if (captured == 0m)
        {
            captured = ParseMoney(dto.SellerReceivableBreakdown?.GrossAmount?.Value);
        }
        if (net == 0m && captured != 0m)
        {
            net = captured - fee;
        }

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException(502, "PayPal capture succeeded but did not return a capture id.");
        }

        _logger.LogInformation("PayPal captured authorization {AuthorizationId} as {CaptureId} status {Status}",
            authorizationId, dto.Id, dto.Status);

        return new PayPalCaptureResult
        {
            CaptureId = dto.Id,
            Status = dto.Status ?? "COMPLETED",
            CapturedAmount = captured,
            PaypalFee = fee,
            NetAmount = net,
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
                body: new { },
                idempotencyKey,
                cancellationToken);
        }
        catch (CheckoutException ex) when (
            ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = Money(amount, currency)
        };

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException(502, "PayPal refund succeeded but did not return a refund id.");
        }

        return new PayPalRefundResult
        {
            RefundId = dto.Id,
            Status = dto.Status ?? "COMPLETED",
            Amount = ParseMoney(dto.Amount?.Value, amount),
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? existingCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(existingCustomerId)
                ? null
                : new PayPalCustomerDto { Id = existingCustomerId },
            PaymentSource = new PayPalPaymentSourceRequest
            {
                Card = new PayPalCardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupRequest,
            idempotencyKey + "-setup",
            cancellationToken);

        EnsureNoPayerAction(setup.Status, setup.Links, "saving a card");

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(502, $"PayPal did not approve the card setup token (status {setup.Status}).");
        }

        if (string.IsNullOrEmpty(setup.Id))
        {
            throw new CheckoutException(502, "PayPal did not return a setup token id.");
        }

        var tokenRequest = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalTokenPaymentSource
            {
                Token = new PayPalTokenReference { Id = setup.Id, Type = "SETUP_TOKEN" }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenRequest,
            idempotencyKey + "-token",
            cancellationToken);

        var customerId = token.Customer?.Id ?? setup.Customer?.Id;
        if (string.IsNullOrEmpty(token.Id) || string.IsNullOrEmpty(customerId))
        {
            throw new CheckoutException(502, "PayPal did not return a payment token and customer id.");
        }

        _logger.LogInformation("Vaulted a PayPal payment token ending {LastDigits} brand {Brand}",
            token.PaymentSource?.Card?.LastDigits, token.PaymentSource?.Card?.Brand);

        return new PayPalVaultedCard
        {
            PaymentTokenId = token.Id,
            CustomerId = customerId,
            LastDigits = token.PaymentSource?.Card?.LastDigits ?? setup.PaymentSource?.Card?.LastDigits,
            Brand = token.PaymentSource?.Card?.Brand ?? setup.PaymentSource?.Card?.Brand,
            Expiry = token.PaymentSource?.Card?.Expiry ?? setup.PaymentSource?.Card?.Expiry,
            CardholderName = token.PaymentSource?.Card?.Name ?? setup.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
            body: null,
            idempotencyKey: null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (windowStart, windowEnd) in SplitIntoThirtyOneDayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatPayPalTime(windowStart);
                var end = FormatPayPalTime(windowEnd);
                var path =
                    "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(start)}" +
                    $"&end_date={Uri.EscapeDataString(end)}" +
                    "&fields=transaction_info" +
                    "&balance_affecting_records_only=N" +
                    "&page_size=500" +
                    $"&page={page}";

                var response = await SendAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    idempotencyKey: null,
                    cancellationToken);

                totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
                foreach (var detail in response.TransactionDetails ?? new List<PayPalTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null)
                    {
                        continue;
                    }

                    var dedupeKey = $"{info.TransactionId}:{info.TransactionEventCode}:{info.TransactionInitiationDate}";
                    if (!seen.Add(dedupeKey))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        PaypalReferenceId = info.PaypalReferenceId,
                        InvoiceId = info.InvoiceId,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = ParseMoneyNullable(info.TransactionAmount?.Value),
                        Fee = ParseMoneyNullable(info.FeeAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        InitiationDate = ParseTime(info.TransactionInitiationDate)
                    });
                }

                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        PayPalPaymentSourceRequest paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var orderRequest = BuildOrderRequest(amount, currency, invoiceId, lines, paymentSource);

        _logger.LogInformation("Creating PayPal AUTHORIZE order for invoice {InvoiceId} amount {Amount} {Currency}",
            invoiceId, FormatAmount(amount), currency);

        var created = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            orderRequest,
            idempotencyKey,
            cancellationToken);

        EnsureNoPayerAction(created.Status, created.Links, "paying for an order");

        var authorization = FirstAuthorization(created);
        if (authorization == null && !string.IsNullOrEmpty(created.Id))
        {
            created = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{Uri.EscapeDataString(created.Id)}/authorize",
                new { },
                idempotencyKey + "-authorize",
                cancellationToken);
            EnsureNoPayerAction(created.Status, created.Links, "paying for an order");
            authorization = FirstAuthorization(created);
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id) || string.IsNullOrEmpty(created.Id))
        {
            throw new CheckoutException(502, "PayPal did not return an authorization hold for the order.");
        }

        _logger.LogInformation(
            "PayPal authorized order {PayPalOrderId} authorization {AuthorizationId} status {Status} lastDigits {LastDigits}",
            created.Id, authorization.Id, authorization.Status, created.PaymentSource?.Card?.LastDigits);

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = created.Id,
            AuthorizationId = authorization.Id,
            Status = authorization.Status ?? "CREATED",
            CreateTime = ParseTime(authorization.CreateTime),
            ExpirationTime = ParseTime(authorization.ExpirationTime),
            CardLastDigits = created.PaymentSource?.Card?.LastDigits,
            CardBrand = created.PaymentSource?.Card?.Brand,
            Amount = ParseMoney(authorization.Amount?.Value, amount),
            Currency = authorization.Amount?.CurrencyCode ?? currency
        };
    }

    private PayPalOrderRequest BuildOrderRequest(
        decimal amount,
        string currency,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> lines,
        PayPalPaymentSourceRequest paymentSource)
    {
        // Amount-only purchase unit matches PayPal's documented single-step card AUTHORIZE
        // sample. PHYSICAL_GOODS line items without a shipping address are refused.
        _ = lines;
        var formatted = FormatAmount(amount);

        return new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PaymentSource = paymentSource,
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceId,
                    CustomId = invoiceId,
                    Amount = new PayPalAmountRequest
                    {
                        CurrencyCode = currency,
                        Value = formatted
                    }
                }
            }
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, Combine(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", Truncate(idempotencyKey, 108));
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || (allowEmpty && string.IsNullOrWhiteSpace(payload)))
        {
            if (allowEmpty)
            {
                return default!;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            if (allowEmpty)
            {
                return default!;
            }

            throw new CheckoutException(502, "PayPal returned an empty success response.");
        }

        var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (parsed == null)
        {
            throw new CheckoutException(502, "PayPal returned a response that could not be read.");
        }

        return parsed;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken!;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken!;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new CheckoutException(500, "PayPal:ClientId and PayPal:ClientSecret must be configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Combine("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with {StatusCode}", (int)response.StatusCode);
                throw new CheckoutException(502, "Unable to authenticate with PayPal.");
            }

            var token = JsonSerializer.Deserialize<PayPalAccessTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new CheckoutException(502, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string Combine(string path)
    {
        var root = _options.ResolveBaseUrl();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return root + path;
    }

    private CheckoutException MapError(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // PayPal sometimes returns non-JSON on gateway failures.
        }

        var issue = error?.Details != null && error.Details.Count > 0
            ? error.Details[0].Issue
            : null;
        var description = error?.Details != null && error.Details.Count > 0
            ? error.Details[0].Description
            : null;
        var message = $"{error?.Name ?? statusCode.ToString()}: {error?.Message ?? "PayPal request failed."}";
        if (!string.IsNullOrEmpty(issue))
        {
            message += $" [{issue}] {description}";
        }

        if (!string.IsNullOrEmpty(error?.DebugId))
        {
            message += $" (debug_id {error.DebugId})";
        }

        var mapped = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        _logger.LogWarning("PayPal API error {StatusCode} {Name} {Issue} debug {DebugId}",
            (int)statusCode, error?.Name, issue, error?.DebugId);

        return new CheckoutException(mapped, message);
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLinkDto>? links, string action)
    {
        if (!string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var approve = links?.Find(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase));

        throw new PayerActionRequiredException(
            $"PayPal required a browser challenge while {action}. This integration does not collect shopper approval in a browser." +
            (approve?.Href != null ? $" PayPal returned a payer-action link." : string.Empty));
    }

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits is { Count: > 0 }
            ? order.PurchaseUnits[0].Payments?.Authorizations is { Count: > 0 }
                ? order.PurchaseUnits[0].Payments!.Authorizations![0]
                : null
            : null;

    private static PayPalAuthorizationDetails MapAuthorizationDetails(PayPalAuthorizationDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new CheckoutException(502, "PayPal authorization details did not include an id.");
        }

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = dto.Id,
            Status = dto.Status ?? "CREATED",
            CreateTime = ParseTime(dto.CreateTime),
            ExpirationTime = ParseTime(dto.ExpirationTime)
        };
    }

    private static PayPalAddressRequest? MapAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return null;
        }

        return new PayPalAddressRequest
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static PayPalMoneyDto Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = FormatAmount(amount)
    };

    private static string FormatAmount(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value, decimal fallback = 0m) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static decimal? ParseMoneyNullable(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatPayPalTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoThirtyOneDayWindows(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (windowStart, windowEnd);
            windowStart = windowEnd;
        }

        if (from == to)
        {
            yield return (from, to);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access-token";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly Regex DigitRun = new("[0-9]{13,19}", RegexOptions.Compiled);
    private static readonly Regex CardNumberJson = new("\"number\"\\s*:\\s*\"[^\"]*\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecurityCodeJson = new("\"security_code\"\\s*:\\s*\"[^\"]*\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PayPalGateway(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        IMemoryCache cache,
        ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PayPalOrderAuthorization> AuthorizeOrderAsync(
        CreateAuthorizedPaymentRequest request,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = "default",
                    ["description"] = Truncate(request.Description, 127),
                    ["custom_id"] = request.CustomId,
                    ["invoice_id"] = request.InvoiceId,
                    ["amount"] = Money(request.Currency, request.Amount)
                }
            }
        };

        var created = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "v2/checkout/orders",
            createBody,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(created.Id))
        {
            throw new PaymentException("PayPal did not return an order id.");
        }

        EnsureNoPayerActionRequired(created);

        var authorizeBody = new Dictionary<string, object?>
        {
            ["payment_source"] = BuildPaymentSource(request)
        };

        var response = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            $"v2/checkout/orders/{created.Id}/authorize",
            authorizeBody,
            payPalRequestId + "-authorize",
            preferRepresentation: true,
            cancellationToken);

        EnsureNoPayerActionRequired(response);
        var authorization = response.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationResource>())
            .FirstOrDefault();

        if (authorization == null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for the order. Status: {response.Status}.");
        }

        return new PayPalOrderAuthorization
        {
            OrderId = created.Id,
            OrderStatus = response.Status ?? created.Status ?? string.Empty,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status ?? string.Empty,
            CreateTime = ParseTime(authorization.CreateTime),
            ExpirationTime = ParseTime(authorization.ExpirationTime),
            Amount = PayPalMoney.Parse(authorization.Amount?.Value),
            Currency = authorization.Amount?.CurrencyCode ?? request.Currency
        };
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            payPalRequestId: null,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal returned an empty authorization.");
        }

        return MapAuthorization(resource);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(currency, amount)
        };

        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal reauthorization did not return an authorization id.");
        }

        return MapAuthorization(resource);
    }

    public async Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string currency,
        decimal amount,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(currency, amount),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        var capture = await SendAsync<PayPalCaptureResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(capture.Id))
        {
            throw new PaymentException("PayPal capture did not return a capture id.");
        }

        var capturedAmount = PayPalMoney.Parse(capture.SellerReceivableBreakdown?.GrossAmount?.Value
                                               ?? capture.Amount?.Value);
        var fee = PayPalMoney.Parse(capture.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = PayPalMoney.Parse(capture.SellerReceivableBreakdown?.NetAmount?.Value);
        if (net == 0m && capturedAmount > 0m)
        {
            net = capturedAmount - fee;
        }

        return new PayPalCaptureDetails
        {
            CaptureId = capture.Id,
            Status = capture.Status ?? string.Empty,
            CapturedAmount = capturedAmount,
            PaypalFee = fee,
            NetProceeds = net,
            Currency = capture.Amount?.CurrencyCode
                       ?? capture.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode
                       ?? currency
        };
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorizationResource>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: new Dictionary<string, object?>(),
                payPalRequestId,
                preferRepresentation: true,
                cancellationToken);
        }
        catch (PaymentException ex) when (
            ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        string currency,
        decimal? amount,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body;
        if (amount.HasValue)
        {
            body = new Dictionary<string, object?>
            {
                ["amount"] = Money(currency, amount.Value)
            };
        }
        else
        {
            body = new Dictionary<string, object?>();
        }

        var refund = await SendAsync<PayPalRefundResource>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            payPalRequestId,
            preferRepresentation: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refund.Id))
        {
            throw new PaymentException("PayPal refund did not return a refund id.");
        }

        return new PayPalRefundDetails
        {
            RefundId = refund.Id,
            Status = refund.Status ?? string.Empty,
            Amount = PayPalMoney.Parse(refund.Amount?.Value),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["merchant_customer_id"] = merchantCustomerId
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardObject(card)
            }
        };

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupBody,
            payPalRequestId + "-setup",
            preferRepresentation: false,
            cancellationToken);

        EnsureNoPayerActionRequired(setup.Status, setup.Links);

        if (string.IsNullOrWhiteSpace(setup.Id))
        {
            throw new PaymentException("PayPal did not return a setup token id.");
        }

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(setup.Status, "VAULTED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(setup.Status, "TOKENIZED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal setup token is not ready to vault (status {setup.Status}).");
        }

        var tokenBody = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["merchant_customer_id"] = merchantCustomerId
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var vaulted = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenBody,
            payPalRequestId + "-token",
            preferRepresentation: false,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(vaulted.Id))
        {
            throw new PaymentException("PayPal did not return a payment token id.");
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = vaulted.Id,
            CustomerId = vaulted.Customer?.Id ?? setup.Customer?.Id,
            Brand = vaulted.PaymentSource?.Card?.Brand,
            LastDigits = vaulted.PaymentSource?.Card?.LastDigits,
            Expiry = vaulted.PaymentSource?.Card?.Expiry,
            CardholderName = vaulted.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            payPalRequestId: null,
            preferRepresentation: false,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var rangeEnd = to.ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        if (rangeEnd > now)
        {
            rangeEnd = now;
        }

        if (windowStart > rangeEnd)
        {
            return results;
        }

        while (windowStart <= rangeEnd)
        {
            // Transaction Search allows at most 31 days per request.
            var windowEnd = windowStart.AddDays(31).AddSeconds(-1);
            if (windowEnd > rangeEnd)
            {
                windowEnd = rangeEnd;
            }

            await ListTransactionsForWindowAsync(windowStart, windowEnd, results, cancellationToken);
            if (windowEnd == rangeEnd)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task ListTransactionsForWindowAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"start_date={Uri.EscapeDataString(FormatTime(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatTime(end))}" +
                $"&fields=all" +
                $"&page_size=100" +
                $"&page={page}" +
                $"&balance_affecting_records_only=N";

            PayPalTransactionSearchResponse response;
            try
            {
                response = await SendAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    "v1/reporting/transactions?" + query,
                    body: null,
                    payPalRequestId: null,
                    preferRepresentation: false,
                    cancellationToken);
            }
            catch (PaymentException ex) when (IsTransactionSearchDataUnavailable(ex))
            {
                // Reporting lags live activity (up to three hours) and sandbox ranges
                // with no indexed data return this error instead of an empty list.
                _logger.LogInformation(
                    "PayPal transaction search has no data for {Start} to {End}: {Message}",
                    start,
                    end,
                    ex.Message);
                return;
            }

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        ReferenceIdType = info.PaypalReferenceIdType,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        InitiationDate = ParseTime(info.TransactionInitiationDate),
                        AmountValue = info.TransactionAmount?.Value,
                        AmountCurrency = info.TransactionAmount?.CurrencyCode,
                        FeeValue = info.FeeAmount?.Value,
                        FeeCurrency = info.FeeAmount?.CurrencyCode
                    });
                }
            }

            totalPages = response.TotalPages ?? page;
            page++;
        } while (page <= totalPages);
    }

    private object BuildPaymentSource(CreateAuthorizedPaymentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?>
                {
                    ["vault_id"] = request.VaultId,
                    ["stored_credential"] = new Dictionary<string, object?>
                    {
                        ["payment_initiator"] = "CUSTOMER",
                        ["payment_type"] = "UNSCHEDULED",
                        ["usage"] = "SUBSEQUENT"
                    }
                }
            };
        }

        if (request.Card == null)
        {
            throw new PaymentException("A card or saved payment method is required to authorize an order.");
        }

        return new Dictionary<string, object?>
        {
            ["card"] = BuildCardObject(request.Card)
        };
    }

    private static Dictionary<string, object?> BuildCardObject(CardPaymentSource card)
    {
        var cardObject = new Dictionary<string, object?>
        {
            ["name"] = card.Name,
            ["number"] = NormalizeCardNumber(card.Number),
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };

        if (card.BillingAddress != null)
        {
            cardObject["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["address_line_2"] = card.BillingAddress.AddressLine2,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = NormalizeCountryCode(card.BillingAddress.CountryCode)
            };
        }

        return cardObject;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        string? payPalRequestId,
        bool preferRepresentation,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        EnsureConfigured();
        var attempt = 0;
        var maxAttempts = 4;
        var refreshedToken = false;

        while (true)
        {
            attempt++;
            using var request = new HttpRequestMessage(method, Combine(relativeUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(payPalRequestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", payPalRequestId);
            }

            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }

            string? redactedRequest = null;
            if (body != null && method != HttpMethod.Get)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                redactedRequest = Redact(json);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await DelayBackoff(attempt, cancellationToken);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (allowEmpty || string.IsNullOrWhiteSpace(payload) || response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default!;
                }

                var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
                if (parsed == null)
                {
                    throw new PaymentException("PayPal returned an empty success body.");
                }

                return parsed;
            }

            var error = TryParseError(payload);
            var debugId = error?.DebugId;
            _logger.LogWarning(
                "PayPal request {Method} {Url} failed with {Status}. debug_id={DebugId} name={Name} request={Request} body={ErrorBody}",
                method,
                relativeUrl,
                (int)response.StatusCode,
                debugId,
                error?.Name,
                redactedRequest,
                Redact(payload));

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
            {
                _cache.Remove(TokenCacheKey);
                refreshedToken = true;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                && attempt < maxAttempts
                && (method == HttpMethod.Get || !string.IsNullOrWhiteSpace(payPalRequestId)))
            {
                await DelayBackoff(attempt, cancellationToken);
                continue;
            }

            throw MapException(response.StatusCode, error, payload);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Combine("v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(payload);
                throw MapException(response.StatusCode, error, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions);
            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new PaymentException("PayPal token response did not include an access token.");
            }

            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(lifetime, 30)));
            return token.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException("PayPal ClientId and ClientSecret are not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentException("PayPal Currency is not configured.");
        }
    }

    private Uri Combine(string relativeUrl)
    {
        var baseUrl = _options.ResolveBaseUrl().TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativeUrl);
    }

    private static Dictionary<string, string> Money(string currency, decimal amount) => new()
    {
        ["currency_code"] = currency.ToUpperInvariant(),
        ["value"] = PayPalMoney.Format(amount, currency)
    };

    private static PayPalAuthorizationDetails MapAuthorization(PayPalAuthorizationResource resource) => new()
    {
        AuthorizationId = resource.Id!,
        Status = resource.Status ?? string.Empty,
        CreateTime = ParseTime(resource.CreateTime),
        ExpirationTime = ParseTime(resource.ExpirationTime),
        Amount = PayPalMoney.Parse(resource.Amount?.Value),
        Currency = resource.Amount?.CurrencyCode ?? string.Empty
    };

    private static void EnsureNoPayerActionRequired(PayPalOrderResponse response)
    {
        EnsureNoPayerActionRequired(response.Status, response.Links);
        if (string.Equals(response.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || HasPayerActionLink(response.Links))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step in the browser (for example 3-D Secure). This integration does not collect that approval.");
        }
    }

    private static void EnsureNoPayerActionRequired(string? status, List<PayPalLinkDto>? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || HasPayerActionLink(links))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step in the browser (for example 3-D Secure). This integration does not collect that approval.");
        }
    }

    private static bool HasPayerActionLink(List<PayPalLinkDto>? links)
    {
        return links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static PayPalErrorBody? TryParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PayPalErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PaymentException MapException(HttpStatusCode statusCode, PayPalErrorBody? error, string payload)
    {
        var detail = error?.Details == null
            ? null
            : string.Join("; ", error.Details.Select(d =>
            {
                var field = string.IsNullOrWhiteSpace(d.Field) ? string.Empty : $" ({d.Field})";
                var description = string.IsNullOrWhiteSpace(d.Description) ? string.Empty : ": " + Redact(d.Description);
                return $"{d.Issue}{field}{description}";
            }));
        var message = error == null
            ? $"PayPal request failed ({(int)statusCode})."
            : $"PayPal {error.Name}: {Redact(error.Message)}{(detail == null ? string.Empty : " — " + detail)}";

        if (statusCode == HttpStatusCode.NotFound)
        {
            return new PaymentNotFoundException(message);
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return new PaymentForbiddenException(message);
        }

        if (statusCode == HttpStatusCode.Conflict || statusCode == HttpStatusCode.UnprocessableEntity)
        {
            return new PaymentConflictException(message);
        }

        return new PaymentException(message, statusCode, error?.DebugId);
    }

    private static bool IsTransactionSearchDataUnavailable(PaymentException ex)
    {
        return ex.Message.Contains("start date is not available", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Data for the given start date", StringComparison.OrdinalIgnoreCase);
    }

    private static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = DigitRun.Replace(value, "****");
        redacted = CardNumberJson.Replace(redacted, "\"number\":\"****\"");
        redacted = SecurityCodeJson.Replace(redacted, "\"security_code\":\"****\"");
        return redacted;
    }

    private static string NormalizeCardNumber(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string NormalizeCountryCode(string country)
    {
        if (string.Equals(country, "USA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(country, "United States", StringComparison.OrdinalIgnoreCase))
        {
            return "US";
        }

        return country.Length == 2 ? country.ToUpperInvariant() : country;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private static string FormatTime(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    private static async Task DelayBackoff(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 250) + Random.Shared.Next(0, 250);
        await Task.Delay(delayMs, cancellationToken);
    }
}

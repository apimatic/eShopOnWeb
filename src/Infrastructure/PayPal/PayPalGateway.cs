using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access-token";
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
        ConfigureHttpClient(_httpClient, _options);
    }

    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency;

    public Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        IReadOnlyList<PurchaseItem> items,
        CardDetails card,
        string requestId,
        CancellationToken cancellationToken = default) =>
        CreateAndAuthorizeAsync(
            invoiceId,
            customId,
            amount,
            items,
            new PayPalPaymentSourceDto
            {
                Card = new PayPalCardDto
                {
                    Name = card.Name,
                    Number = DigitsOnly(card.Number),
                    Expiry = NormalizeExpiry(card.Expiry),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            requestId,
            cancellationToken);

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        IReadOnlyList<PurchaseItem> items,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default) =>
        CreateAndAuthorizeAsync(
            invoiceId,
            customId,
            amount,
            items,
            new PayPalPaymentSourceDto
            {
                Card = new PayPalCardDto
                {
                    VaultId = vaultId,
                    StoredCredential = new PayPalStoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME",
                        Usage = "SUBSEQUENT"
                    }
                }
            },
            requestId,
            cancellationToken);

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorizationDetails(auth);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequestDto { Amount = ToPayPalMoney(amount) },
            requestId,
            cancellationToken);

        return new AuthorizationResult(
            string.Empty,
            auth.Id ?? throw Failed("reauthorization id"),
            auth.Status ?? "CREATED",
            ParseTime(auth.ExpirationTime),
            ToMoney(auth.Amount, amount.Currency));
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequestDto { Amount = ToPayPalMoney(amount), FinalCapture = true },
            requestId,
            cancellationToken);

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id ?? throw Failed("capture id"),
            capture.Status ?? "COMPLETED",
            ToMoney(breakdown?.GrossAmount ?? capture.Amount, amount.Currency),
            breakdown?.PaypalFee is null ? null : ToMoney(breakdown.PaypalFee, amount.Currency),
            breakdown?.NetAmount is null ? null : ToMoney(breakdown.NetAmount, amount.Currency));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            null,
            requestId,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        MoneyAmount amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var refund = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequestDto { Amount = ToPayPalMoney(amount) },
            requestId,
            cancellationToken);

        return new RefundResult(
            refund.Id ?? throw Failed("refund id"),
            refund.Status ?? "COMPLETED",
            ToMoney(refund.Amount, amount.Currency));
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var token = await SendAsync<PayPalPaymentTokenDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            new PayPalVaultRequestDto
            {
                PaymentSource = new PayPalPaymentSourceDto
                {
                    Card = new PayPalCardDto
                    {
                        Name = card.Name,
                        Number = DigitsOnly(card.Number),
                        Expiry = NormalizeExpiry(card.Expiry),
                        SecurityCode = card.SecurityCode,
                        BillingAddress = MapAddress(card.BillingAddress)
                    }
                },
                Customer = string.IsNullOrWhiteSpace(paypalCustomerId)
                    ? new PayPalCustomerDto { MerchantCustomerId = merchantCustomerId }
                    : new PayPalCustomerDto { Id = paypalCustomerId }
            },
            requestId,
            cancellationToken);

        var vaulted = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id ?? throw Failed("payment token id"),
            token.Customer?.Id,
            vaulted?.LastDigits ?? card.LastFour,
            vaulted?.Brand ?? "CARD",
            vaulted?.Expiry ?? card.Expiry,
            vaulted?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalPaymentTokenDto>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            null,
            null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitDateRange(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var path =
                    $"v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                    $"&page_size=500&page={page}&fields=all";

                var response = await SendAsync<PayPalSearchResponseDto>(HttpMethod.Get, path, null, null, cancellationToken);
                foreach (var detail in response.TransactionDetails ?? [])
                {
                    var info = detail.TransactionInfo;
                    if (string.IsNullOrEmpty(info?.TransactionId))
                    {
                        continue;
                    }

                    DateTimeOffset? initiated = DateTimeOffset.TryParse(
                        info.TransactionInitiationDate,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out var parsed)
                        ? parsed
                        : null;

                    var currency = info.TransactionAmount?.CurrencyCode ?? _options.Currency;
                    results.Add(new ReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.CustomField,
                        info.InvoiceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionAmount is null
                            ? null
                            : ToMoney(new PayPalMoneyDto { CurrencyCode = currency, Value = info.TransactionAmount.Value ?? "0" }, currency),
                        info.FeeAmount is null
                            ? null
                            : ToMoney(new PayPalMoneyDto { CurrencyCode = info.FeeAmount.CurrencyCode ?? currency, Value = info.FeeAmount.Value ?? "0" }, currency),
                        initiated));
                }

                totalPages = Math.Max(response.TotalPages, 1);
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<AuthorizationResult> CreateAndAuthorizeAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        IReadOnlyList<PurchaseItem> items,
        PayPalPaymentSourceDto paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            new PayPalCreateOrderRequestDto
            {
                Intent = "AUTHORIZE",
                PurchaseUnits =
                [
                    new PayPalPurchaseUnitRequestDto
                    {
                        InvoiceId = Truncate(invoiceId, 127),
                        CustomId = Truncate(customId, 127),
                        Amount = new PayPalAmountDto
                        {
                            CurrencyCode = amount.Currency,
                            Value = PayPalJson.MoneyValue(amount.Value),
                            Breakdown = new PayPalAmountBreakdownDto { ItemTotal = ToPayPalMoney(amount) }
                        },
                        Items = items.Select(i => new PayPalItemDto
                        {
                            Name = Truncate(i.Name, 127),
                            Quantity = i.Quantity,
                            UnitAmount = ToPayPalMoney(i.UnitAmount)
                        }).ToList()
                    }
                ],
                PaymentSource = paymentSource
            },
            requestId,
            cancellationToken);

        EnsureNoPayerChallenge(order);

        var authorization = FirstAuthorization(order);
        if (authorization is null && !string.IsNullOrEmpty(order.Id))
        {
            order = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"v2/checkout/orders/{order.Id}/authorize",
                new { },
                $"{requestId}-authorize",
                cancellationToken);
            EnsureNoPayerChallenge(order);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException("PayPal did not return an authorization for this payment.", 502);
        }

        var authorizedAmount = ToMoney(authorization.Amount, amount.Currency);
        if (authorizedAmount.Value != amount.Value)
        {
            throw new PaymentException(
                $"PayPal authorized {authorizedAmount.Value} {authorizedAmount.Currency} but the order total is {amount.Value} {amount.Currency}.",
                502);
        }

        return new AuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status ?? "CREATED",
            ParseTime(authorization.ExpirationTime),
            authorizedAmount);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmpty = false) where T : class, new()
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", Truncate(requestId, 108));
        }

        if (method == HttpMethod.Post)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: PayPalJson.Options);
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, path);

        using var response = await SendHttpAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            if (allowEmpty)
            {
                return new T();
            }

            return new T();
        }

        return JsonSerializer.Deserialize<T>(payload, PayPalJson.Options) ?? new T();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured.", 500);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

            _logger.LogInformation("PayPal POST v1/oauth2/token");
            using var response = await SendHttpAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw MapError(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalOAuthTokenDto>(payload, PayPalJson.Options);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new PaymentException("PayPal did not return an access token.", 502);
            }

            _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30)));
            return token.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static void EnsureNoPayerChallenge(PayPalOrderDto order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal required a shopper to complete a browser challenge (for example 3-D Secure). This API does not collect a browser approval round-trip.");
        }
    }

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderDto order) =>
        order.PurchaseUnits?.SelectMany(unit => unit.Payments?.Authorizations ?? []).FirstOrDefault();

    private static AuthorizationDetails MapAuthorizationDetails(PayPalAuthorizationDto auth) =>
        new(auth.Id ?? throw Failed("authorization id"), auth.Status ?? "UNKNOWN", ParseTime(auth.ExpirationTime), ToMoney(auth.Amount, auth.Amount?.CurrencyCode ?? "USD"));

    private static PayPalAddressDto MapAddress(BillingAddress? address) =>
        new()
        {
            AddressLine1 = string.IsNullOrWhiteSpace(address?.AddressLine1) ? "123 Main St" : address!.AddressLine1,
            AddressLine2 = address?.AddressLine2,
            AdminArea2 = string.IsNullOrWhiteSpace(address?.AdminArea2) ? "San Jose" : address!.AdminArea2,
            AdminArea1 = string.IsNullOrWhiteSpace(address?.AdminArea1) ? "CA" : address!.AdminArea1,
            PostalCode = string.IsNullOrWhiteSpace(address?.PostalCode) ? "95131" : address!.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address?.CountryCode) ? "US" : address!.CountryCode
        };

    private static string NormalizeExpiry(string expiry)
    {
        var digits = new string(expiry.Where(char.IsDigit).ToArray());
        if (digits.Length == 6)
        {
            return digits.StartsWith("20", StringComparison.Ordinal)
                ? $"{digits[..4]}-{digits[4..]}"
                : $"{digits[2..]}-{digits[..2]}";
        }

        if (digits.Length == 4)
        {
            return $"20{digits[2..]}-{digits[..2]}";
        }

        return expiry.Length >= 7 ? expiry[..7] : expiry;
    }

    private static PayPalMoneyDto ToPayPalMoney(MoneyAmount amount) =>
        new() { CurrencyCode = amount.Currency, Value = PayPalJson.MoneyValue(amount.Value) };

    private static MoneyAmount ToMoney(PayPalMoneyDto? money, string fallbackCurrency) =>
        new(PayPalJson.ParseMoney(money?.Value), string.IsNullOrWhiteSpace(money?.CurrencyCode) ? fallbackCurrency : money.CurrencyCode);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string DigitsOnly(string number) => new(number.Where(char.IsDigit).ToArray());

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static Exception Failed(string what) => new PaymentException($"PayPal response was missing {what}.", 502);

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            yield break;
        }

        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }

        if (from == to)
        {
            yield return (from, to);
        }
    }

    private static void ConfigureHttpClient(HttpClient httpClient, PayPalOptions options)
    {
        httpClient.BaseAddress ??= new Uri(options.ResolveBaseUrl().TrimEnd('/') + "/");
        httpClient.DefaultRequestVersion = HttpVersion.Version11;
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        if (httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
        catch (InvalidOperationException)
        {
            // IHttpClientFactory may reuse a started client; timeout is already configured.
        }
    }

    private async Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var detail = Innermost(ex).Message;
            throw new PaymentException($"Unable to reach PayPal: {detail}", ex, 502);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentException("PayPal request timed out.", ex, 504);
        }
        catch (InvalidOperationException ex)
        {
            throw new PaymentException($"PayPal request could not be sent: {Innermost(ex).Message}", ex, 502);
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private PaymentException MapError(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(payload, PayPalJson.Options);
        }
        catch (JsonException)
        {
            _logger.LogWarning("PayPal returned a non-JSON error with status {StatusCode}", (int)statusCode);
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var message = error?.Message ?? "PayPal request failed.";
        var detail = string.IsNullOrWhiteSpace(issue)
            ? message
            : $"{message} ({issue}{(description is null ? string.Empty : $": {description}")})";

        _logger.LogWarning(
            "PayPal error status {StatusCode} name {Name} debugId {DebugId} issue {Issue}",
            (int)statusCode,
            error?.Name,
            error?.DebugId,
            issue);

        var mapped = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.UnprocessableEntity => 422,
            _ => 502
        };

        return new PaymentException(detail, mapped);
    }
}

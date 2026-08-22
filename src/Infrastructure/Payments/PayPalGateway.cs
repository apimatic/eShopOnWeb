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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
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

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Currency => _options.Currency;

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string idempotencyKey,
        CardPaymentSource? card,
        string? vaultId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildPaymentSource(card, vaultId);
        var body = new PayPalCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            [
                new PayPalPurchaseUnit
                {
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = new PayPalMoney
                    {
                        CurrencyCode = currency,
                        Value = MoneyFormatter.ToPayPalValue(amount, currency)
                    }
                }
            ],
            PaymentSource = paymentSource
        };

        var response = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var order = Deserialize<PayPalOrderResponse>(response);
        ThrowIfPayerActionRequired(order.Status, "authorizing the card payment");

        var authorization = order.PurchaseUnits?[0].Payments?.Authorizations?[0];
        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new OrderPaymentException(502,
                $"PayPal did not return an authorization for order {order.Id} (status {order.Status}).");
        }

        return ToAuthorization(order.Id, authorization, currency);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        var authorization = Deserialize<PayPalAuthorizationResource>(response);
        return ToAuthorization(null, authorization, authorization.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest
        {
            Amount = new PayPalMoney
            {
                CurrencyCode = currency,
                Value = MoneyFormatter.ToPayPalValue(amount, currency)
            }
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId: $"reauth-{authorizationId}",
            preferRepresentation: true,
            cancellationToken);

        var authorization = Deserialize<PayPalAuthorizationResource>(response);
        return ToAuthorization(null, authorization, currency);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        try
        {
            var response = await SendAsync(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/capture",
                body,
                idempotencyKey,
                preferRepresentation: true,
                cancellationToken);

            return ToCapture(Deserialize<PayPalCaptureResource>(response));
        }
        catch (OrderPaymentException ex) when (ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        return ToCapture(Deserialize<PayPalCaptureResource>(response));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                requestId: $"void-{authorizationId}",
                preferRepresentation: false,
                cancellationToken);
        }
        catch (OrderPaymentException ex) when (
            ex.StatusCode == 404 ||
            ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            // Already released — treat as success for idempotent cancel.
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new PayPalRefundRequest
            {
                Amount = new PayPalMoney
                {
                    CurrencyCode = currency,
                    Value = MoneyFormatter.ToPayPalValue(amount.Value, currency)
                }
            }
            : new { };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var refund = Deserialize<PayPalRefundResource>(response);
        return new PayPalRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? string.Empty,
            MoneyFormatter.Parse(refund.Amount?.Value),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? payPalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var setupBody = new PayPalSetupTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(payPalCustomerId)
                ? null
                : new PayPalCustomer { Id = payPalCustomerId },
            PaymentSource = new PayPalPaymentSource
            {
                Card = ToVaultCard(card)
            }
        };

        var setupResponse = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupBody,
            $"{idempotencyKey}-setup",
            preferRepresentation: true,
            cancellationToken);

        var setup = Deserialize<PayPalSetupTokenResponse>(setupResponse);
        ThrowIfPayerActionRequired(setup.Status, "saving the card");

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(setup.Id))
        {
            throw new OrderPaymentException(502,
                $"PayPal did not approve the card for vaulting (status {setup.Status}).");
        }

        var tokenBody = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Token = new PayPalTokenSource
                {
                    Id = setup.Id,
                    Type = "SETUP_TOKEN"
                }
            }
        };

        var tokenResponse = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenBody,
            $"{idempotencyKey}-token",
            preferRepresentation: true,
            cancellationToken);

        var token = Deserialize<PayPalPaymentTokenResponse>(tokenResponse);
        if (string.IsNullOrEmpty(token.Id))
        {
            throw new OrderPaymentException(502, "PayPal did not return a payment token for the saved card.");
        }

        var vaultCard = token.PaymentSource?.Card;
        return new PayPalVaultedCard(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id,
            vaultCard?.LastDigits ?? card.LastDigits,
            vaultCard?.Brand ?? "CARD",
            vaultCard?.Expiry ?? card.Expiry,
            vaultCard?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{vaultId}",
                body: null,
                requestId: null,
                preferRepresentation: false,
                cancellationToken);
        }
        catch (OrderPaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed from the vault.
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to, TimeSpan.FromDays(31)))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = Uri.EscapeDataString(windowStart.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var end = Uri.EscapeDataString(windowEnd.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                var path =
                    $"/v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size=500&page={page}";

                var response = await SendAsync(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    preferRepresentation: false,
                    cancellationToken);

                var pageResult = Deserialize<PayPalSearchResponse>(response);
                if (pageResult.TransactionDetails != null)
                {
                    foreach (var detail in pageResult.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null || string.IsNullOrEmpty(info.TransactionId))
                        {
                            continue;
                        }

                        DateTimeOffset? initiated = null;
                        if (DateTimeOffset.TryParse(info.TransactionInitiationDate, out var parsed))
                        {
                            initiated = parsed;
                        }

                        results.Add(new PayPalReportedTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.InvoiceId,
                            info.CustomField,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            info.TransactionAmount == null ? null : MoneyFormatter.Parse(info.TransactionAmount.Value),
                            info.TransactionAmount?.CurrencyCode,
                            initiated,
                            info.FeeAmount == null ? null : MoneyFormatter.Parse(info.FeeAmount.Value)));
                    }
                }

                totalPages = pageResult.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxWindow)
    {
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }

        if (cursor == from)
        {
            yield return (from, to);
        }
    }

    private PayPalPaymentSource BuildPaymentSource(CardPaymentSource? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new PayPalPaymentSource
            {
                Card = new PayPalCard
                {
                    VaultId = vaultId
                }
            };
        }

        if (card == null)
        {
            throw new OrderPaymentException(400, "Card details or a saved payment method are required.");
        }

        return new PayPalPaymentSource
        {
            Card = ToCard(card)
        };
    }

    private static PayPalCard ToCard(CardPaymentSource card)
    {
        return new PayPalCard
        {
            Number = card.Number.Replace(" ", string.Empty),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToBillingAddress(card.BillingAddress)
        };
    }

    private static PayPalCard ToVaultCard(CardPaymentSource card)
    {
        var paypalCard = ToCard(card);
        paypalCard.ExperienceContext = new PayPalExperienceContext
        {
            BrandName = "eShopOnWeb",
            Locale = "en-US",
            ReturnUrl = "https://example.com/return",
            CancelUrl = "https://example.com/cancel"
        };
        return paypalCard;
    }

    private static PayPalAddress? ToBillingAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return new PayPalAddress
            {
                AddressLine1 = "123 Main St.",
                AdminArea2 = "Kent",
                AdminArea1 = "OH",
                PostalCode = "44240",
                CountryCode = "US"
            };
        }

        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode
        };
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        if (trimmed.Contains('/'))
        {
            var parts = trimmed.Split('/');
            if (parts.Length == 2 && parts[0].Length <= 2 && parts[1].Length == 4)
            {
                return $"{parts[1]}-{parts[0].PadLeft(2, '0')}";
            }
        }

        return trimmed;
    }

    private static PayPalAuthorizationResult ToAuthorization(
        string? orderId,
        PayPalAuthorizationResource authorization,
        string fallbackCurrency)
    {
        return new PayPalAuthorizationResult(
            orderId,
            authorization.Id ?? string.Empty,
            authorization.Status ?? string.Empty,
            ParseTime(authorization.ExpirationTime),
            MoneyFormatter.Parse(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? fallbackCurrency);
    }

    private static PayPalCaptureResult ToCapture(PayPalCaptureResource capture)
    {
        var breakdown = capture.SellerReceivableBreakdown;
        var captured = MoneyFormatter.Parse(capture.Amount?.Value);
        if (captured == 0m)
        {
            captured = MoneyFormatter.Parse(breakdown?.GrossAmount?.Value);
        }

        var fee = MoneyFormatter.Parse(breakdown?.PaypalFee?.Value);
        var net = breakdown?.NetAmount != null
            ? MoneyFormatter.Parse(breakdown.NetAmount.Value)
            : captured - fee;
        if (net < 0m)
        {
            net = 0m;
        }

        return new PayPalCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? string.Empty,
            captured,
            fee,
            net,
            capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty,
            capture.SupplementaryData?.RelatedIds?.AuthorizationId);
    }

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static void ThrowIfPayerActionRequired(string? status, string action)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper browser challenge while {action}. " +
                "This integration does not complete 3-D Secure or hosted approval round-trips.");
        }
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, Combine(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.Add("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.Add("Prefer", "return=representation");
        }

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal HTTP call to {Method} {Path} failed before a response was received.", method, path);
            throw new OrderPaymentException(502, "The PayPal request failed before a response was received.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        var error = TryDeserializeError(payload);
        var issue = FormatPayPalError(error, payload);
        _logger.LogWarning(
            "PayPal {Method} {Path} returned {Status}. DebugId {DebugId}. {Issue}",
            method,
            path,
            (int)response.StatusCode,
            error?.DebugId,
            issue);

        var statusCode = MapStatusCode(response.StatusCode);
        throw new OrderPaymentException(statusCode, issue);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60))
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new OrderPaymentException(500, "PayPal client credentials are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Combine("/v1/oauth2/token"));
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayPal token request failed before a response was received.");
                throw new OrderPaymentException(502, "The PayPal token request failed before a response was received.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryDeserializeError(payload);
                throw new OrderPaymentException(502,
                    $"PayPal authentication failed ({(int)response.StatusCode}). {FormatPayPalError(error, payload)}");
            }

            var token = Deserialize<PayPalTokenResponse>(payload);
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new OrderPaymentException(502, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri Combine(string path)
    {
        var baseUrl = _options.ResolveBaseUrl();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return new Uri(baseUrl + path, UriKind.Absolute);
    }

    private static T Deserialize<T>(string payload)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (result == null)
            {
                throw new OrderPaymentException(502, "PayPal returned an empty response.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new OrderPaymentException(502, $"PayPal returned a response that could not be parsed: {ex.Message}");
        }
    }

    private static PayPalErrorResponse? TryDeserializeError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatPayPalError(PayPalErrorResponse? error, string raw)
    {
        if (error == null)
        {
            return string.IsNullOrWhiteSpace(raw) ? "PayPal returned an error with no body." : "PayPal returned an error.";
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(error.Name))
        {
            builder.Append(error.Name);
        }

        if (!string.IsNullOrEmpty(error.Message))
        {
            if (builder.Length > 0) builder.Append(": ");
            builder.Append(error.Message);
        }

        if (error.Details != null)
        {
            foreach (var detail in error.Details)
            {
                if (!string.IsNullOrEmpty(detail.Issue))
                {
                    builder.Append(" [");
                    builder.Append(detail.Issue);
                    if (!string.IsNullOrEmpty(detail.Description))
                    {
                        builder.Append(": ");
                        builder.Append(detail.Description);
                    }

                    builder.Append(']');
                }
            }
        }

        if (!string.IsNullOrEmpty(error.DebugId))
        {
            builder.Append(" (debug_id ");
            builder.Append(error.DebugId);
            builder.Append(')');
        }

        return builder.Length == 0 ? "PayPal returned an error." : builder.ToString();
    }

    private static int MapStatusCode(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.UnprocessableEntity => 409,
            _ => 502
        };

    private sealed class PayPalTokenResponse
    {
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class PayPalCreateOrderRequest
    {
        public string Intent { get; set; } = "AUTHORIZE";
        public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
        public PayPalPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalPurchaseUnit
    {
        public string? InvoiceId { get; set; }
        public string? CustomId { get; set; }
        public PayPalMoney? Amount { get; set; }
        public PayPalPaymentsContainer? Payments { get; set; }
    }

    private sealed class PayPalMoney
    {
        public string? CurrencyCode { get; set; }
        public string? Value { get; set; }
    }

    private sealed class PayPalPaymentSource
    {
        public PayPalCard? Card { get; set; }
        public PayPalTokenSource? Token { get; set; }
    }

    private sealed class PayPalCard
    {
        public string? Number { get; set; }
        public string? Expiry { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public string? VaultId { get; set; }
        public PayPalAddress? BillingAddress { get; set; }
        public PayPalExperienceContext? ExperienceContext { get; set; }
    }

    private sealed class PayPalTokenSource
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
    }

    private sealed class PayPalAddress
    {
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? AdminArea2 { get; set; }
        public string? AdminArea1 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private sealed class PayPalExperienceContext
    {
        public string? BrandName { get; set; }
        public string? Locale { get; set; }
        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }
    }

    private sealed class PayPalCustomer
    {
        public string? Id { get; set; }
    }

    private sealed class PayPalOrderResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    }

    private sealed class PayPalPaymentsContainer
    {
        public List<PayPalAuthorizationResource>? Authorizations { get; set; }
    }

    private sealed class PayPalAuthorizationResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public string? ExpirationTime { get; set; }
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalReauthorizeRequest
    {
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalCaptureRequest
    {
        public string? InvoiceId { get; set; }
        public bool FinalCapture { get; set; }
    }

    private sealed class PayPalCaptureResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoney? Amount { get; set; }
        public PayPalSellerReceivableBreakdown? SellerReceivableBreakdown { get; set; }
        public PayPalSupplementaryData? SupplementaryData { get; set; }
    }

    private sealed class PayPalSupplementaryData
    {
        public PayPalRelatedIds? RelatedIds { get; set; }
    }

    private sealed class PayPalRelatedIds
    {
        public string? OrderId { get; set; }
        public string? AuthorizationId { get; set; }
    }

    private sealed class PayPalSellerReceivableBreakdown
    {
        public PayPalMoney? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")]
        public PayPalMoney? PaypalFee { get; set; }
        public PayPalMoney? NetAmount { get; set; }
    }

    private sealed class PayPalRefundRequest
    {
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalRefundResource
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalMoney? Amount { get; set; }
    }

    private sealed class PayPalSetupTokenRequest
    {
        public PayPalCustomer? Customer { get; set; }
        public PayPalPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalSetupTokenResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public PayPalCustomer? Customer { get; set; }
    }

    private sealed class PayPalPaymentTokenRequest
    {
        public PayPalPaymentSource? PaymentSource { get; set; }
    }

    private sealed class PayPalPaymentTokenResponse
    {
        public string? Id { get; set; }
        public PayPalCustomer? Customer { get; set; }
        public PayPalPaymentSourceResponse? PaymentSource { get; set; }
    }

    private sealed class PayPalPaymentSourceResponse
    {
        public PayPalVaultedCardResource? Card { get; set; }
    }

    private sealed class PayPalVaultedCardResource
    {
        public string? LastDigits { get; set; }
        public string? Brand { get; set; }
        public string? Expiry { get; set; }
        public string? Name { get; set; }
    }

    private sealed class PayPalSearchResponse
    {
        public List<PayPalTransactionDetail>? TransactionDetails { get; set; }
        public int? TotalPages { get; set; }
        public int? Page { get; set; }
    }

    private sealed class PayPalTransactionDetail
    {
        public PayPalTransactionInfo? TransactionInfo { get; set; }
    }

    private sealed class PayPalTransactionInfo
    {
        public string? TransactionId { get; set; }
        public string? PaypalReferenceId { get; set; }
        public string? InvoiceId { get; set; }
        public string? CustomField { get; set; }
        public string? TransactionEventCode { get; set; }
        public string? TransactionStatus { get; set; }
        public string? TransactionInitiationDate { get; set; }
        public PayPalMoney? TransactionAmount { get; set; }
        public PayPalMoney? FeeAmount { get; set; }
    }

    private sealed class PayPalErrorResponse
    {
        public string? Name { get; set; }
        public string? Message { get; set; }
        public string? DebugId { get; set; }
        public List<PayPalErrorDetail>? Details { get; set; }
    }

    private sealed class PayPalErrorDetail
    {
        public string? Issue { get; set; }
        public string? Description { get; set; }
        public string? Field { get; set; }
    }
}

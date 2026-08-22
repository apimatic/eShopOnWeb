using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly PayPalAccessTokenCache _tokenCache;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        PayPalAccessTokenCache tokenCache,
        ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokenCache = tokenCache;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<PayPalOrderResult> CreateAuthorizedOrderAsync(
        string payPalRequestId,
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        CardDetails? card,
        string? vaultId,
        CancellationToken cancellationToken = default)
    {
        var body = new OrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new()
                {
                    Amount = new MoneyDto
                    {
                        CurrencyCode = currency,
                        Value = PayPalConfiguration.FormatMoney(amount)
                    },
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Description = "eShopOnWeb order"
                }
            },
            PaymentSource = new PaymentSourceDto
            {
                Card = BuildCardRequest(card, vaultId)
            }
        };

        OrderResponseDto response;
        try
        {
            response = await SendAsync<OrderResponseDto>(
                HttpMethod.Post,
                "/v2/checkout/orders",
                body,
                payPalRequestId,
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (
            body.PaymentSource?.Card?.StoredCredential != null &&
            (ex.StatusCode is 400 or 422))
        {
            body.PaymentSource.Card.StoredCredential = null;
            response = await SendAsync<OrderResponseDto>(
                HttpMethod.Post,
                "/v2/checkout/orders",
                body,
                payPalRequestId + "-sc",
                cancellationToken);
        }

        return ToOrderResult(response);
    }

    public async Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<OrderResponseDto>(
            HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}",
            body: null,
            payPalRequestId: null,
            cancellationToken);

        return ToOrderResult(response);
    }

    public async Task<PayPalOrderResult> AuthorizeOrderAsync(
        string orderId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<OrderResponseDto>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/authorize",
            body: new { },
            payPalRequestId,
            cancellationToken);

        return ToOrderResult(response);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            payPalRequestId: null,
            cancellationToken);

        return ToAuthorizationResult(response);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequestDto
        {
            Amount = new MoneyDto
            {
                CurrencyCode = currency,
                Value = PayPalConfiguration.FormatMoney(amount)
            }
        };

        var response = await SendAsync<AuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            payPalRequestId,
            cancellationToken);

        return ToAuthorizationResult(response);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: new { },
            payPalRequestId,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequestDto
        {
            Amount = new MoneyDto
            {
                CurrencyCode = currency,
                Value = PayPalConfiguration.FormatMoney(amount)
            },
            FinalCapture = true
        };

        var response = await SendAsync<CaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            payPalRequestId,
            cancellationToken);

        var result = ToCaptureResult(response);
        return await GetCaptureAsync(result.Id, cancellationToken);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<CaptureDto>(
            HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            body: null,
            payPalRequestId: null,
            cancellationToken);

        return ToCaptureResult(response);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new RefundRequestDto
        {
            Amount = new MoneyDto
            {
                CurrencyCode = currency,
                Value = PayPalConfiguration.FormatMoney(amount)
            }
        };

        var response = await SendAsync<RefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            payPalRequestId,
            cancellationToken);

        return new PayPalRefundResult
        {
            Id = response.Id ?? throw new PayPalGatewayException("PayPal refund response did not include an id."),
            Status = response.Status ?? "UNKNOWN",
            Amount = PayPalConfiguration.ParseMoney(response.Amount?.Value),
            Currency = response.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(
        string payPalRequestId,
        string customerId,
        string merchantCustomerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequestDto
        {
            Customer = new VaultCustomerDto
            {
                Id = customerId,
                MerchantCustomerId = SanitizeMerchantCustomerId(merchantCustomerId)
            },
            PaymentSource = new VaultPaymentSourceDto
            {
                Card = new VaultCardRequestDto
                {
                    Name = card.Name,
                    Number = NormalizeCardNumber(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToBillingAddress(card)
                }
            }
        };

        PaymentTokenResponseDto response;
        try
        {
            response = await SendAsync<PaymentTokenResponseDto>(
                HttpMethod.Post,
                "/v3/vault/payment-tokens",
                body,
                payPalRequestId,
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (
            ex.StatusCode is 400 or 422 &&
            (ex.PayPalIssue?.Contains("CUSTOMER", StringComparison.OrdinalIgnoreCase) == true ||
             (ex.Message?.Contains("customer", StringComparison.OrdinalIgnoreCase) == true)))
        {
            body.Customer = new VaultCustomerDto { MerchantCustomerId = SanitizeMerchantCustomerId(merchantCustomerId) };
            response = await SendAsync<PaymentTokenResponseDto>(
                HttpMethod.Post,
                "/v3/vault/payment-tokens",
                body,
                payPalRequestId + "-c",
                cancellationToken);
        }

        return new PayPalPaymentTokenResult
        {
            Id = response.Id ?? throw new PayPalGatewayException("PayPal vault response did not include a payment token id."),
            CustomerId = response.Customer?.Id,
            Brand = response.PaymentSource?.Card?.Brand,
            LastDigits = response.PaymentSource?.Card?.LastDigits,
            Expiry = response.PaymentSource?.Card?.Expiry,
            Name = response.PaymentSource?.Card?.Name
        };
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
                body: null,
                payPalRequestId: null,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token {TokenId} was already absent.", paymentTokenId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var window in SplitIntoSearchWindows(start, end))
        {
            await CollectWindowAsync(window.Start, window.End, results, cancellationToken);
        }

        return results;
    }

    private async Task CollectWindowAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        List<PayPalReportedTransaction> results,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int? totalPages = null;
        var pageCount = 0;
        do
        {
            var path =
                "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(PayPalConfiguration.ToRfc3339(start))}" +
                $"&end_date={Uri.EscapeDataString(PayPalConfiguration.ToRfc3339(end))}" +
                "&fields=all" +
                "&balance_affecting_records_only=N" +
                "&page_size=500" +
                $"&page={page}";

            SearchResponseDto response;
            try
            {
                response = await SendAsync<SearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    payPalRequestId: null,
                    cancellationToken);
            }
            catch (PayPalGatewayException ex) when (
                string.Equals(ex.PayPalName, "RESULTSET_TOO_LARGE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ex.PayPalIssue, "RESULTSET_TOO_LARGE", StringComparison.OrdinalIgnoreCase))
            {
                if (end - start <= TimeSpan.FromHours(1))
                {
                    throw;
                }

                var midpoint = start + (end - start) / 2;
                await CollectWindowAsync(start, midpoint, results, cancellationToken);
                await CollectWindowAsync(midpoint, end, results, cancellationToken);
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

                    DateTimeOffset? initiation = null;
                    if (!string.IsNullOrWhiteSpace(info.TransactionInitiationDate) &&
                        DateTimeOffset.TryParse(info.TransactionInitiationDate, out var parsed))
                    {
                        initiation = parsed;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        PayPalReferenceId = info.PaypalReferenceId,
                        CustomField = info.CustomField,
                        InvoiceId = info.InvoiceId,
                        TransactionEventCode = info.TransactionEventCode,
                        TransactionStatus = info.TransactionStatus,
                        Amount = info.TransactionAmount?.Value,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        InitiationDate = initiation
                    });
                }
            }

            totalPages = response.TotalPages;
            pageCount = response.TransactionDetails?.Count ?? 0;
            page++;
        } while (totalPages.HasValue ? page <= totalPages.Value : pageCount >= 500);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoSearchWindows(
        DateTimeOffset start,
        DateTimeOffset end)
    {
        const int maxDays = 31;
        var cursor = start;
        while (cursor < end)
        {
            var windowEnd = cursor.AddDays(maxDays);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }
    }

    private CardRequestDto BuildCardRequest(CardDetails? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new CardRequestDto
            {
                VaultId = vaultId,
                StoredCredential = new StoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            };
        }

        if (card == null)
        {
            throw new PaymentValidationException("Card details or a saved payment method are required.");
        }

        return new CardRequestDto
        {
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToBillingAddress(card)
        };
    }

    private static BillingAddressDto ToBillingAddress(CardDetails card) =>
        new()
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = card.AdminArea2,
            AdminArea1 = card.AdminArea1,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };

    private static string NormalizeCardNumber(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string SanitizeMerchantCustomerId(string value)
    {
        var filtered = new string(value.Where(c =>
            char.IsLetterOrDigit(c) || "-_.^*$@#".Contains(c)).ToArray());
        return filtered.Length <= 64 ? filtered : filtered[..64];
    }

    private PayPalOrderResult ToOrderResult(OrderResponseDto response)
    {
        var status = response.Status ?? "UNKNOWN";
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper approval step (for example 3-D Secure) that cannot be completed without a browser. The integration does not implement an approval round-trip.");
        }

        var authorization = response.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationDto>())
            .FirstOrDefault();

        DateTimeOffset? expiration = null;
        if (!string.IsNullOrWhiteSpace(authorization?.ExpirationTime) &&
            DateTimeOffset.TryParse(authorization.ExpirationTime, out var parsedExpiration))
        {
            expiration = parsedExpiration;
        }

        return new PayPalOrderResult
        {
            Id = response.Id ?? throw new PayPalGatewayException("PayPal order response did not include an id."),
            Status = status,
            AuthorizationId = authorization?.Id,
            AuthorizationStatus = authorization?.Status,
            AuthorizedAmount = authorization?.Amount?.Value is null
                ? null
                : PayPalConfiguration.ParseMoney(authorization.Amount.Value),
            Currency = authorization?.Amount?.CurrencyCode,
            AuthorizationExpiration = expiration,
            PayerActionRequired = false
        };
    }

    private static PayPalAuthorizationResult ToAuthorizationResult(AuthorizationDto response)
    {
        DateTimeOffset? expiration = null;
        DateTimeOffset? created = null;
        if (!string.IsNullOrWhiteSpace(response.ExpirationTime) &&
            DateTimeOffset.TryParse(response.ExpirationTime, out var parsedExpiration))
        {
            expiration = parsedExpiration;
        }

        if (!string.IsNullOrWhiteSpace(response.CreateTime) &&
            DateTimeOffset.TryParse(response.CreateTime, out var parsedCreated))
        {
            created = parsedCreated;
        }

        return new PayPalAuthorizationResult
        {
            Id = response.Id ?? throw new PayPalGatewayException("PayPal authorization response did not include an id."),
            Status = response.Status ?? "UNKNOWN",
            Amount = response.Amount?.Value is null ? null : PayPalConfiguration.ParseMoney(response.Amount.Value),
            Currency = response.Amount?.CurrencyCode,
            ExpirationTime = expiration,
            CreateTime = created
        };
    }

    private static PayPalCaptureResult ToCaptureResult(CaptureDto response)
    {
        decimal? amount = null;
        if (!string.IsNullOrWhiteSpace(response.SellerReceivableBreakdown?.GrossAmount?.Value))
        {
            amount = PayPalConfiguration.ParseMoney(response.SellerReceivableBreakdown.GrossAmount.Value);
        }
        else if (!string.IsNullOrWhiteSpace(response.Amount?.Value))
        {
            amount = PayPalConfiguration.ParseMoney(response.Amount.Value);
        }

        return new PayPalCaptureResult
        {
            Id = response.Id ?? throw new PayPalGatewayException("PayPal capture response did not include an id."),
            Status = response.Status ?? "UNKNOWN",
            Amount = amount,
            PayPalFee = response.SellerReceivableBreakdown?.PaypalFee?.Value is null
                ? null
                : PayPalConfiguration.ParseMoney(response.SellerReceivableBreakdown.PaypalFee.Value),
            NetAmount = response.SellerReceivableBreakdown?.NetAmount?.Value is null
                ? null
                : PayPalConfiguration.ParseMoney(response.SellerReceivableBreakdown.NetAmount.Value),
            Currency = response.Amount?.CurrencyCode
                ?? response.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode,
            AuthorizationId = response.SupplementaryData?.RelatedIds?.AuthorizationId
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? payPalRequestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var url = _options.ResolveBaseUrl() + path;
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(payPalRequestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", TruncateRequestId(payPalRequestId));
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, PayPalJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal HTTP call to {Path} failed before a response was received.", path);
            throw new PayPalGatewayException($"PayPal request to {method} {path} failed: {ex.Message}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            if (allowNoContent && (response.StatusCode == System.Net.HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content)))
            {
                return default!;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return default!;
            }

            var parsed = JsonSerializer.Deserialize<T>(content, PayPalJson.Options);
            if (parsed == null)
            {
                throw new PayPalGatewayException($"PayPal returned an empty body for {method} {path}.");
            }

            return parsed;
        }

        throw ToGatewayException(response.StatusCode, content, method, path);
    }

    private PayPalGatewayException ToGatewayException(
        System.Net.HttpStatusCode statusCode,
        string content,
        HttpMethod method,
        string path)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Body is not the documented error shape; continue with status only.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var debugId = error?.DebugId;
        _logger.LogWarning(
            "PayPal {Method} {Path} failed with {StatusCode}. name={Name} issue={Issue} debugId={DebugId}",
            method,
            path,
            (int)statusCode,
            error?.Name,
            issue,
            debugId);

        var message = error?.Message;
        if (!string.IsNullOrWhiteSpace(issue) || !string.IsNullOrWhiteSpace(description))
        {
            message = $"{error?.Name}: {issue} {description}".Trim();
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"PayPal request {method} {path} failed with HTTP {(int)statusCode}.";
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            message += $" PayPal debug id: {debugId}.";
        }

        return new PayPalGatewayException(message, (int)statusCode, debugId, error?.Name, issue);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache.TryGet(out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PayPalGatewayException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
        }

        var url = _options.ResolveBaseUrl() + "/v1/oauth2/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
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
            _logger.LogWarning(ex, "PayPal token request failed before a response was received.");
            throw new PayPalGatewayException($"PayPal token request failed: {ex.Message}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToGatewayException(response.StatusCode, content, HttpMethod.Post, "/v1/oauth2/token");
        }

        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(content, PayPalJson.Options);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new PayPalGatewayException("PayPal token response did not include an access_token.");
        }

        var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
        _tokenCache.Set(token.AccessToken, TimeSpan.FromSeconds(Math.Max(lifetime, 30)));
        return token.AccessToken;
    }

    private static string TruncateRequestId(string value) =>
        value.Length <= 108 ? value : value[..108];
}

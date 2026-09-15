using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency;

    public async Task<PayPalOrderResult> CreateOrderAsync(
        decimal amount,
        string customId,
        string invoiceId,
        CardDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Amount = new PayPalAmountWithBreakdownDto
                    {
                        CurrencyCode = Currency,
                        Value = PaymentFormatting.FormatAmount(amount, Currency)
                    }
                }
            },
            PaymentSource = BuildPaymentSource(card, vaultId)
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        return MapOrder(order);
    }

    public async Task<PayPalOrderResult> AuthorizeOrderAsync(
        string paypalOrderId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            $"v2/checkout/orders/{paypalOrderId}/authorize",
            new { },
            requestId,
            cancellationToken,
            preferRepresentation: true);

        return MapOrder(order);
    }

    public async Task<PayPalOrderResult> GetOrderAsync(
        string paypalOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Get,
            $"v2/checkout/orders/{paypalOrderId}",
            null,
            requestId: null,
            cancellationToken);

        return MapOrder(order);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequestDto
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = Currency,
                Value = PaymentFormatting.FormatAmount(amount, Currency)
            }
        };

        var authorization = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        return MapAuthorization(authorization);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            null,
            requestId,
            cancellationToken,
            preferRepresentation: true,
            allowNoContent: true);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequestDto
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = Currency,
                Value = PaymentFormatting.FormatAmount(amount, Currency)
            },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken,
            preferRepresentation: true);

        return MapCapture(capture);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new PayPalRefundRequestDto
            {
                Amount = new PayPalMoneyDto
                {
                    CurrencyCode = Currency,
                    Value = PaymentFormatting.FormatAmount(amount.Value, Currency)
                }
            }
            : new { };

        var paypalRequestId = $"{captureId}:{idempotencyKey}";
        if (paypalRequestId.Length > 108)
        {
            paypalRequestId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paypalRequestId)))[..64];
        }

        var refund = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            paypalRequestId,
            cancellationToken,
            preferRepresentation: true);

        return new PayPalRefundResult
        {
            Id = refund.Id ?? string.Empty,
            Status = refund.Status ?? string.Empty,
            Amount = PaymentFormatting.ParseAmount(refund.Amount?.Value)
        };
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string customerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentTokenRequest = new PayPalPaymentTokenRequestDto
        {
            Customer = new PayPalCustomerDto { Id = customerId },
            PaymentSource = new PayPalVaultPaymentSourceDto
            {
                Card = BuildCardDto(card)
            }
        };

        try
        {
            var token = await SendAsync<PayPalPaymentTokenResponseDto>(
                HttpMethod.Post,
                "v3/vault/payment-tokens",
                paymentTokenRequest,
                requestId,
                cancellationToken);

            return MapVaultedCard(token);
        }
        catch (PaymentException ex) when (ShouldFallBackToSetupToken(ex))
        {
            _logger.LogInformation("Direct vault failed; creating a setup token instead. PayPal message: {Message}", ex.Message);

            var setupRequest = new PayPalPaymentTokenRequestDto
            {
                Customer = new PayPalCustomerDto { Id = customerId },
                PaymentSource = new PayPalVaultPaymentSourceDto
                {
                    Card = BuildCardDto(card)
                }
            };

            var setup = await SendAsync<PayPalPaymentTokenResponseDto>(
                HttpMethod.Post,
                "v3/vault/setup-tokens",
                setupRequest,
                requestId + "-setup",
                cancellationToken);

            EnsureNoPayerAction(setup.Status, setup.Links);

            if (string.IsNullOrWhiteSpace(setup.Id))
            {
                throw new PaymentException("PayPal did not return a setup token id.");
            }

            var exchange = new PayPalPaymentTokenRequestDto
            {
                PaymentSource = new PayPalVaultPaymentSourceDto
                {
                    Token = new PayPalVaultTokenRequestDto
                    {
                        Id = setup.Id,
                        Type = "SETUP_TOKEN"
                    }
                }
            };

            var vaulted = await SendAsync<PayPalPaymentTokenResponseDto>(
                HttpMethod.Post,
                "v3/vault/payment-tokens",
                exchange,
                requestId + "-token",
                cancellationToken);

            return MapVaultedCard(vaulted);
        }
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{vaultId}",
            null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitInto31DayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatRfc3339(windowStart);
                var end = FormatRfc3339(windowEnd);
                var path =
                    $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=transaction_info&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    null,
                    requestId: null,
                    cancellationToken);

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
                            TransactionId = info.TransactionId ?? string.Empty,
                            ReferenceId = info.PaypalReferenceId,
                            CustomField = info.CustomField,
                            InvoiceId = info.InvoiceId,
                            EventCode = info.TransactionEventCode,
                            Status = info.TransactionStatus,
                            Amount = info.TransactionAmount?.Value,
                            Currency = info.TransactionAmount?.CurrencyCode,
                            FeeAmount = info.FeeAmount?.Value,
                            InitiationDate = ParseTimestamp(info.TransactionInitiationDate)
                        });
                    }
                }

                totalPages = response.TotalPages.GetValueOrDefault(1);
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private PayPalPaymentSourceDto? BuildPaymentSource(CardDetails? card, string? vaultId)
    {
        if (card == null && string.IsNullOrWhiteSpace(vaultId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new PayPalPaymentSourceDto
            {
                Card = new PayPalCardRequestDto
                {
                    VaultId = vaultId,
                    StoredCredential = new PayPalStoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "ONE_TIME",
                        Usage = "SUBSEQUENT"
                    },
                    Attributes = new PayPalCardAttributesDto
                    {
                        Verification = new PayPalCardVerificationDto { Method = "SCA_WHEN_REQUIRED" }
                    }
                }
            };
        }

        return new PayPalPaymentSourceDto
        {
            Card = BuildCardDto(card!)
        };
    }

    private static PayPalCardRequestDto BuildCardDto(CardDetails card)
    {
        return new PayPalCardRequestDto
        {
            Name = card.Name,
            Number = card.Number?.Replace(" ", string.Empty, StringComparison.Ordinal),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                },
            Attributes = new PayPalCardAttributesDto
            {
                Verification = new PayPalCardVerificationDto { Method = "SCA_WHEN_REQUIRED" }
            }
        };
    }

    private static PayPalOrderResult MapOrder(PayPalOrderDto order)
    {
        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault();

        var payerAction = string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
                          || (order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (payerAction)
        {
            throw new PayerActionRequiredException();
        }

        return new PayPalOrderResult
        {
            Id = order.Id ?? string.Empty,
            Status = order.Status ?? string.Empty,
            PayerActionRequired = false,
            AuthorizationId = authorization?.Id,
            AuthorizationStatus = authorization?.Status,
            AuthorizationExpiration = ParseTimestamp(authorization?.ExpirationTime)
        };
    }

    private static PayPalAuthorizationResult MapAuthorization(PayPalAuthorizationDto authorization)
    {
        return new PayPalAuthorizationResult
        {
            Id = authorization.Id ?? string.Empty,
            Status = authorization.Status ?? string.Empty,
            ExpirationTime = ParseTimestamp(authorization.ExpirationTime),
            Amount = authorization.Amount?.Value == null ? null : PaymentFormatting.ParseAmount(authorization.Amount.Value)
        };
    }

    private static PayPalCaptureResult MapCapture(PayPalCaptureDto capture)
    {
        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            Id = capture.Id ?? string.Empty,
            Status = capture.Status ?? string.Empty,
            CapturedAmount = PaymentFormatting.ParseAmount(breakdown?.GrossAmount?.Value ?? capture.Amount?.Value),
            PaypalFee = breakdown?.PaypalFee?.Value == null ? null : PaymentFormatting.ParseAmount(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount?.Value == null ? null : PaymentFormatting.ParseAmount(breakdown.NetAmount.Value),
            Currency = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode
        };
    }

    private static VaultedCardResult MapVaultedCard(PayPalPaymentTokenResponseDto token)
    {
        EnsureNoPayerAction(token.Status, token.Links);

        var card = token.PaymentSource?.Card;
        return new VaultedCardResult
        {
            VaultId = token.Id ?? string.Empty,
            CustomerId = token.Customer?.Id ?? string.Empty,
            LastDigits = card?.LastDigits ?? string.Empty,
            Brand = card?.Brand ?? string.Empty,
            Expiry = card?.Expiry ?? string.Empty,
            CardholderName = card?.Name,
            PayerActionRequired = false
        };
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLinkDto>? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false))
        {
            throw new PayerActionRequiredException();
        }
    }

    private static bool ShouldFallBackToSetupToken(PaymentException exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("UNPROCESSABLE", StringComparison.OrdinalIgnoreCase)
               || message.Contains("setup token", StringComparison.OrdinalIgnoreCase)
               || message.Contains("PAYMENT_SOURCE", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitInto31DayWindows(
        DateTimeOffset from,
        DateTimeOffset to)
    {
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
    }

    private static string FormatRfc3339(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool preferRepresentation = false,
        bool allowNoContent = false) where T : class
    {
        await EnsureAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal HTTP call to {Path} failed before a response was received.", relativePath);
            throw new PaymentException("Unable to reach PayPal. Try again shortly.", ex, HttpStatusCode.BadGateway);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
        {
            if (allowNoContent && response.IsSuccessStatusCode)
            {
                return Activator.CreateInstance<T>();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapError(response.StatusCode, payload);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response.StatusCode, payload);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? Activator.CreateInstance<T>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal returned a success status from {Path} but the body could not be parsed.", relativePath);
            throw new PaymentException("PayPal returned an unexpected response.", ex, HttpStatusCode.BadGateway);
        }
    }

    private Exception MapError(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Body is not the documented error model; fall through with a generic message.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var name = error?.Name;
        var message = error?.Message;

        _logger.LogWarning(
            "PayPal API error {Status} name={Name} issue={Issue} debugId={DebugId}",
            (int)statusCode,
            name,
            issue,
            error?.DebugId);

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusCode.ToString(), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PayerActionRequiredException(description);
        }

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized => HttpStatusCode.BadGateway,
            HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            HttpStatusCode.NotFound => HttpStatusCode.Conflict,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            (HttpStatusCode)422 => HttpStatusCode.Conflict,
            _ => HttpStatusCode.BadGateway
        };

        var detail = string.Join(" ", new[] { name, message, issue, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = $"PayPal returned HTTP {(int)statusCode}.";
        }

        return new PaymentException(detail, mappedStatus);
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
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
                _logger.LogError(ex, "PayPal token request failed before a response was received.");
                throw new PaymentException("Unable to reach PayPal to obtain an access token.", ex, HttpStatusCode.BadGateway);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with HTTP {Status}.", (int)response.StatusCode);
                throw new PaymentException("PayPal rejected the client credentials.", HttpStatusCode.BadGateway);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponseDto>(payload, JsonOptions);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                throw new PaymentException("PayPal did not return an access token.", HttpStatusCode.BadGateway);
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 0 ? token.ExpiresIn : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

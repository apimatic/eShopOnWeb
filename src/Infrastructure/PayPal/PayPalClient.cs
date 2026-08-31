using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST client covering OAuth, Orders v2 (authorize), Payments v2
/// (capture/reauthorize/void/refund), Vault v3 (setup tokens, payment tokens)
/// and Transaction Search v1. Card details flow through requests only; they are
/// never logged or stored.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient http, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _http.BaseAddress = new Uri(settings.ResolveBaseUrl());
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, CardDetails card, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            PurchaseUnits = { new PayPalPurchaseUnitRequest { Amount = Money(amount, currency), InvoiceId = invoiceId } },
            PaymentSource = new PayPalPaymentSource { Card = ToCardSource(card) }
        };
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);
        return ToOrderResult(response);
    }

    public async Task<PayPalOrderResult> CreateOrderWithVaultedCardAsync(decimal amount, string currency, string vaultTokenId, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalCreateOrderRequest
        {
            PurchaseUnits = { new PayPalPurchaseUnitRequest { Amount = Money(amount, currency), InvoiceId = invoiceId } },
            PaymentSource = new PayPalPaymentSource { Card = new PayPalCardSource { VaultId = vaultTokenId } }
        };
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);
        return ToOrderResult(response);
    }

    private static PayPalOrderResult ToOrderResult(PayPalOrderResponse response)
    {
        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        return new PayPalOrderResult
        {
            OrderId = response.Id,
            Status = response.Status,
            Authorization = authorization == null ? null : ToAuthorizationResult(authorization)
        };
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize", new { }, requestId, cancellationToken);

        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization == null)
        {
            throw new PayPalApiException(HttpStatusCode.OK, "UNEXPECTED_RESPONSE",
                $"PayPal order {payPalOrderId} authorized but returned no authorization record (order status {response.Status}).", null);
        }
        return ToAuthorizationResult(authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}", null, null, cancellationToken);
        return ToAuthorizationResult(dto);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalCaptureDto>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new PayPalCaptureRequest(), requestId, cancellationToken);

        // The capture response carries only id/status/links; the amounts and the
        // fee breakdown come from the show-capture-details call.
        var details = await SendAsync<PayPalCaptureDto>(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(dto.Id)}", null, null, cancellationToken);

        return new PayPalCaptureResult
        {
            CaptureId = details.Id,
            Status = details.Status,
            GrossAmount = ParseAmount(details.SellerReceivableBreakdown?.GrossAmount ?? details.Amount),
            Currency = (details.SellerReceivableBreakdown?.GrossAmount ?? details.Amount)?.CurrencyCode ?? string.Empty,
            PayPalFee = ParseNullableAmount(details.SellerReceivableBreakdown?.PayPalFee),
            NetAmount = ParseNullableAmount(details.SellerReceivableBreakdown?.NetAmount)
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new PayPalReauthorizeRequest { Amount = Money(amount, currency) }, requestId, cancellationToken);
        return ToAuthorizationResult(dto);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void", null, requestId, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = amount.HasValue ? Money(amount.Value, currency) : null
        };
        var dto = await SendAsync<PayPalRefundDto>(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund", request, idempotencyKey, cancellationToken);

        return new PayPalRefundResult
        {
            RefundId = dto.Id,
            Status = dto.Status,
            Amount = ParseNullableAmount(dto.Amount),
            Currency = dto.Amount?.CurrencyCode
        };
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string? payPalCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalSetupTokenRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = ToCardSource(card) },
            Customer = string.IsNullOrEmpty(payPalCustomerId) ? null : new PayPalCustomer { Id = payPalCustomerId }
        };
        var response = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post, "/v3/vault/setup-tokens", request, requestId, cancellationToken);
        return new PayPalSetupTokenResult
        {
            SetupTokenId = response.Id,
            Status = response.Status,
            CustomerId = response.Customer?.Id ?? string.Empty
        };
    }

    public async Task<PayPalVaultedCard> CreatePaymentTokenAsync(string setupTokenId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalPaymentSource
            {
                Token = new PayPalTokenSource { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };
        var response = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);
        return new PayPalVaultedCard
        {
            VaultTokenId = response.Id,
            CustomerId = response.Customer?.Id ?? string.Empty,
            Brand = response.PaymentSource?.Card?.Brand,
            LastDigits = response.PaymentSource?.Card?.LastDigits,
            Expiry = response.PaymentSource?.Card?.Expiry,
            CardholderName = response.PaymentSource?.Card?.Name
        };
    }

    public async Task DeletePaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        const int pageSize = 500;
        var page = 1;
        var totalPages = 1;

        // Page through the whole range, not just the first page.
        while (page <= totalPages)
        {
            var path = "/v1/reporting/transactions"
                + $"?start_date={Uri.EscapeDataString(FormatPayPalDate(from))}"
                + $"&end_date={Uri.EscapeDataString(FormatPayPalDate(to))}"
                + "&fields=transaction_info"
                + $"&page_size={pageSize}&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
            totalPages = Math.Max(response.TotalPages, 1);

            foreach (var detail in response.TransactionDetails ?? new List<PayPalTransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info == null) continue;
                results.Add(new PayPalTransaction
                {
                    TransactionId = info.TransactionId,
                    EventCode = info.TransactionEventCode,
                    Status = info.TransactionStatus,
                    Amount = ParseNullableAmount(info.TransactionAmount),
                    Currency = info.TransactionAmount?.CurrencyCode,
                    FeeAmount = ParseNullableAmount(info.FeeAmount),
                    InitiationDate = ParsePayPalDate(info.TransactionInitiationDate),
                    UpdatedDate = ParsePayPalDate(info.TransactionUpdatedDate)
                });
            }

            page++;
        }

        return results;
    }

    private static PayPalCardSource ToCardSource(CardDetails card)
    {
        return new PayPalCardSource
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null ? null : new PayPalAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    private static PayPalAuthorizationResult ToAuthorizationResult(PayPalAuthorizationDto dto)
    {
        return new PayPalAuthorizationResult
        {
            AuthorizationId = dto.Id,
            Status = dto.Status,
            Amount = ParseAmount(dto.Amount),
            Currency = dto.Amount?.CurrencyCode ?? string.Empty,
            ExpirationTime = ParsePayPalDate(dto.ExpirationTime)
        };
    }

    private static PayPalMoney Money(decimal amount, string currency)
    {
        return new PayPalMoney
        {
            CurrencyCode = currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }

    private static decimal ParseAmount(PayPalMoney? money)
    {
        return ParseNullableAmount(money) ?? 0m;
    }

    private static decimal? ParseNullableAmount(PayPalMoney? money)
    {
        if (money == null || string.IsNullOrWhiteSpace(money.Value))
        {
            return null;
        }
        return decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static string FormatPayPalDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParsePayPalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // PayPal returns e.g. 2026-08-31T04:03:52+0000 or ...Z
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, requestId, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }
        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, cancellationToken);
            }

            var token = (await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(JsonOptions, cancellationToken))!;
            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task<PayPalApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? name = null;
        string? debugId = null;
        var message = response.ReasonPhrase ?? "PayPal request failed.";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
            if (error != null)
            {
                name = error.Name;
                debugId = error.DebugId;
                if (!string.IsNullOrEmpty(error.Message))
                {
                    message = error.Message;
                }
                var detail = error.Details?.FirstOrDefault(d => !string.IsNullOrEmpty(d.Description));
                if (detail != null)
                {
                    message = $"{message} {detail.Issue}: {detail.Description}";
                }
            }
        }
        catch (JsonException)
        {
            // Keep the reason phrase if the error body is not JSON.
        }
        return new PayPalApiException(response.StatusCode, name, message, debugId);
    }
}

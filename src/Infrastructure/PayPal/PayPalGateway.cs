using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount, string currency, string invoiceId, CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default)
        => AuthorizeAsync(amount, currency, invoiceId, new { card = BuildCardObject(card) }, requestId, cancellationToken);

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        decimal amount, string currency, string invoiceId, string vaultId, string requestId, CancellationToken cancellationToken = default)
        => AuthorizeAsync(amount, currency, invoiceId, new { card = new { vault_id = vaultId } }, requestId, cancellationToken);

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return new PayPalAuthorizationDetails
        {
            AuthorizationId = resource.Id ?? authorizationId,
            Status = resource.Status ?? string.Empty,
            CreatedAt = ParseTime(resource.CreateTime) ?? DateTimeOffset.UtcNow,
            ExpiresAt = ParseTime(resource.ExpirationTime),
            Amount = ToMoney(resource.Amount)
        };
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = PayPalMoneyFormat.ToValue(amount) } };
        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken);
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = resource.Id ?? authorizationId,
            Status = resource.Status ?? "CREATED",
            CreatedAt = ParseTime(resource.CreateTime) ?? DateTimeOffset.UtcNow,
            ExpiresAt = ParseTime(resource.ExpirationTime),
            Amount = ToMoney(resource.Amount)
        };
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalCaptureResource>(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", new { final_capture = true }, requestId, cancellationToken);

        return new PayPalCaptureResult
        {
            CaptureId = resource.Id ?? string.Empty,
            Status = resource.Status ?? string.Empty,
            CapturedAmount = PayPalMoneyFormat.Parse(resource.Amount?.Value),
            PaypalFee = resource.SellerReceivableBreakdown?.PaypalFee is null
                ? null
                : PayPalMoneyFormat.Parse(resource.SellerReceivableBreakdown.PaypalFee.Value),
            NetAmount = resource.SellerReceivableBreakdown?.NetAmount is null
                ? null
                : PayPalMoneyFormat.Parse(resource.SellerReceivableBreakdown.NetAmount.Value),
            Currency = resource.Amount?.CurrencyCode ?? _options.Currency
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", new { }, requestId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new { amount = new { currency_code = currency, value = PayPalMoneyFormat.ToValue(amount) } };
        var resource = await SendAsync<PayPalRefundResource>(
            HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        return new PayPalRefundResult
        {
            RefundId = resource.Id ?? string.Empty,
            Status = resource.Status ?? string.Empty,
            Amount = PayPalMoneyFormat.Parse(resource.Amount?.Value),
            Currency = resource.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        CardPaymentDetails card, string? paypalCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        object setupBody = paypalCustomerId is null
            ? new { payment_source = new { card = BuildCardObject(card) } }
            : new
            {
                customer = new { id = paypalCustomerId },
                payment_source = new { card = BuildCardObject(card) }
            };

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post, "v3/vault/setup-tokens", setupBody, requestId + "-setup", cancellationToken);
        EnsureNoPayerAction(setup.Status, setup.Links, "vault setup-token");

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException($"PayPal did not approve the card for vaulting (status {setup.Status}).");
        }

        var tokenBody = new
        {
            payment_source = new { token = new { id = setup.Id, type = "SETUP_TOKEN" } }
        };
        var paymentToken = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post, "v3/vault/payment-tokens", tokenBody, requestId + "-token", cancellationToken);

        return new PayPalVaultedCardResult
        {
            PaymentTokenId = paymentToken.Id ?? string.Empty,
            CustomerId = paymentToken.Customer?.Id ?? setup.Customer?.Id,
            Brand = paymentToken.PaymentSource?.Card?.Brand ?? "CARD",
            Last4 = paymentToken.PaymentSource?.Card?.LastDigits ?? string.Empty,
            Expiry = paymentToken.PaymentSource?.Card?.Expiry ?? NormalizeExpiry(card.Expiry),
            CardholderName = paymentToken.PaymentSource?.Card?.Name ?? card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}", null, null, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Vault token {PaymentTokenId} was already deleted at PayPal.", paymentTokenId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (chunkFrom, chunkTo) in SplitInto31DayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = Uri.EscapeDataString(chunkFrom.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                var end = Uri.EscapeDataString(chunkTo.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                var path = $"v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size=500&page={page}";
                var pageResult = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
                totalPages = Math.Max(pageResult.TotalPages, 1);
                if (pageResult.TransactionDetails is not null)
                {
                    foreach (var detail in pageResult.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId ?? string.Empty,
                            PaypalReferenceId = info.PaypalReferenceId,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            EventCode = info.TransactionEventCode,
                            Status = info.TransactionStatus,
                            InitiationDate = ParseTime(info.TransactionInitiationDate),
                            Amount = info.TransactionAmount is null ? null : PayPalMoneyFormat.Parse(info.TransactionAmount.Value),
                            Currency = info.TransactionAmount?.CurrencyCode,
                            Fee = info.FeeAmount is null ? null : PayPalMoneyFormat.Parse(info.FeeAmount.Value)
                        });
                    }
                }

                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount, string currency, string invoiceId, object paymentSource, string requestId, CancellationToken cancellationToken)
    {
        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    invoice_id = invoiceId,
                    custom_id = invoiceId,
                    amount = new { currency_code = currency, value = PayPalMoneyFormat.ToValue(amount) }
                }
            },
            payment_source = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post, "v2/checkout/orders", createBody, requestId, cancellationToken);
        EnsureNoPayerAction(order.Status, order.Links, "checkout order");

        var authorization = FirstAuthorization(order);
        if (authorization is null && !string.IsNullOrEmpty(order.Id)
            && !string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            order = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post, $"v2/checkout/orders/{order.Id}/authorize", new { }, requestId + "-auth", cancellationToken);
            EnsureNoPayerAction(order.Status, order.Links, "authorize order");
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException("PayPal did not return an authorization for the card payment.");
        }

        var authorizedAmount = ToMoney(authorization.Amount);
        if (PayPalMoneyFormat.Parse(authorizedAmount.Value) != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
        {
            throw new PaymentException(
                $"PayPal held {authorizedAmount.Value} {authorizedAmount.CurrencyCode} but the order total is {PayPalMoneyFormat.ToValue(amount)} {currency}.");
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = order.Id ?? string.Empty,
            AuthorizationId = authorization.Id,
            Status = authorization.Status ?? order.Status ?? "CREATED",
            CreatedAt = ParseTime(authorization.CreateTime) ?? DateTimeOffset.UtcNow,
            ExpiresAt = ParseTime(authorization.ExpirationTime),
            Amount = authorizedAmount,
            CardBrand = order.PaymentSource?.Card?.Brand,
            CardLast4 = order.PaymentSource?.Card?.LastDigits
        };
    }

    private static PayPalAuthorizationResource? FirstAuthorization(PayPalOrderResponse order)
        => order.PurchaseUnits is { Count: > 0 }
            ? order.PurchaseUnits[0].Payments?.Authorizations is { Count: > 0 } auths ? auths[0] : null
            : null;

    private static object BuildCardObject(CardPaymentDetails card)
    {
        var expiry = NormalizeExpiry(card.Expiry);
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        object? billing = card.BillingAddress is null
            ? null
            : new
            {
                address_line_1 = card.BillingAddress.AddressLine1,
                address_line_2 = card.BillingAddress.AddressLine2,
                admin_area_2 = card.BillingAddress.AdminArea2,
                admin_area_1 = card.BillingAddress.AdminArea1,
                postal_code = card.BillingAddress.PostalCode,
                country_code = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) ? "US" : card.BillingAddress.CountryCode
            };

        return new
        {
            number,
            expiry,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    internal static string NormalizeExpiry(string expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        if (trimmed.Length == 5 && trimmed[2] == '/')
        {
            return $"20{trimmed[3..]}-{trimmed[..2]}";
        }

        if (trimmed.Length == 7 && trimmed[2] == '/')
        {
            return $"{trimmed[3..]}-{trimmed[..2]}";
        }

        throw new PaymentException("Card expiry must be YYYY-MM.");
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLink>? links, string operation)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || links?.Exists(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper challenge for {operation}. This integration does not collect browser approval.");
        }
    }

    private static PayPalMoney ToMoney(PayPalAmount? amount) => new()
    {
        CurrencyCode = amount?.CurrencyCode ?? string.Empty,
        Value = amount?.Value ?? "0.00"
    };

    private static DateTimeOffset? ParseTime(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitInto31DayWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor < to)
        {
            var chunkEnd = cursor.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (cursor, chunkEnd);
            cursor = chunkEnd;
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, path);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
        {
            if (response.IsSuccessStatusCode)
            {
                return default!;
            }

            throw new PaymentException($"PayPal returned {(int)response.StatusCode} with no body.", response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = SafeDeserialize<PayPalErrorResponse>(payload);
            var detail = error?.Details is { Count: > 0 }
                ? string.Join("; ", error.Details.ConvertAll(d => $"{d.Issue}: {d.Description}"))
                : error?.Message ?? "PayPal request failed.";
            _logger.LogWarning("PayPal error {Status} debug_id={DebugId} name={Name}", (int)response.StatusCode, error?.DebugId, error?.Name);
            var mapped = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                ? HttpStatusCode.BadRequest
                : HttpStatusCode.BadGateway;
            throw new PaymentException($"PayPal error ({error?.Name ?? response.StatusCode.ToString()}): {detail}", mapped);
        }

        if (typeof(T) == typeof(object))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
               ?? throw new PaymentException("PayPal returned an empty response body.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException("PayPal ClientId and ClientSecret are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal token request failed with {Status}.", (int)response.StatusCode);
                throw new PaymentException("Unable to authenticate with PayPal.", HttpStatusCode.BadGateway);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions)
                        ?? throw new PaymentException("PayPal token response was empty.");
            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static T? SafeDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}

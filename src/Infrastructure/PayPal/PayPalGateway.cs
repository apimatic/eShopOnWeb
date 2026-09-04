using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal adapter behind IPayPalGateway. Business rejections (declines, payer
/// challenges) surface as PaymentDeclinedException; transport failures and unusable
/// responses surface as PayPalGatewayException. Request payloads (which contain card
/// data) are never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    public const string HttpClientName = "PayPalServerSdk";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PayPalSettings> _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly object _clientLock = new();
    private PayPalServerSdkClient? _client;

    public PayPalGateway(IHttpClientFactory httpClientFactory, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private PayPalServerSdkClient Client => _client ??= BuildClient();

    private PayPalServerSdkClient BuildClient()
    {
        var settings = _settings.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new PayPalGatewayException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret in configuration.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(20)
            },
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            }
        };

        // The SDK in this release only models Sandbox on ServerEnvironment; production and
        // custom hosts are selected by overriding the base URL, which routes every call -
        // including the token request.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }
        else if (string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com";
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        return new PayPalServerSdkClient(httpClient, options);
    }

    public async Task<GatewayAuthorizeResult> AuthorizeAsync(GatewayAuthorizeRequest request, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("authorize payment", async token =>
        {
            try
            {
                var orderBody = new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = request.Amount.Currency,
                                Value = FormatAmount(request.Amount.Amount)
                            }
                        }
                    },
                    PaymentSource = new PaymentSource
                    {
                        Card = BuildCardRequest(request)
                    }
                };

                var order = await Client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderBody,
                    prefer: "return=minimal",
                    requestOptions: null,
                    ct: token);

                if (order.Status != null && order.Status.Value == "PAYER_ACTION_REQUIRED")
                {
                    return new GatewayAuthorizeResult(false, order.Id, null, null, null, request.Amount,
                        "PayPal requires cardholder approval in a browser.", RequiresPayerAction: true);
                }

                var authorized = await Client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);

                if (authorized.Status != null && authorized.Status.Value == "PAYER_ACTION_REQUIRED")
                {
                    return new GatewayAuthorizeResult(false, order.Id, null, null, null, request.Amount,
                        "PayPal requires cardholder approval in a browser.", RequiresPayerAction: true);
                }

                var authorization = authorized.PurchaseUnits?
                    .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

                if (authorization?.Id is null)
                {
                    return new GatewayAuthorizeResult(false, order.Id, null, authorized.Status?.Value, null,
                        request.Amount, "PayPal did not return a payment authorization.", RequiresPayerAction: false);
                }

                var authStatus = authorization.Status?.Value;
                if (authStatus == "DENIED" || authStatus == "VOIDED")
                {
                    return new GatewayAuthorizeResult(false, order.Id, null, authStatus, null, request.Amount,
                        "The card issuer declined the payment.", RequiresPayerAction: false);
                }

                return new GatewayAuthorizeResult(
                    Success: true,
                    PayPalOrderId: order.Id,
                    AuthorizationId: authorization.Id,
                    Status: authStatus,
                    ExpiresAt: ParseDate(authorization.ExpirationTime),
                    Amount: ToGatewayMoney(authorization.Amount) ?? request.Amount,
                    DeclineReason: null,
                    RequiresPayerAction: false);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("creating the PayPal order", typed);
                throw ClassifyApiError("creating the PayPal order", ex.Error);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("authorizing the payment", typed);
                throw ClassifyApiError("authorizing the payment", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayAuthorizeResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        return await CallAsync("read payment authorization", async token =>
        {
            try
            {
                var authorization = await Client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: token);

                return new GatewayAuthorizeResult(
                    Success: true,
                    PayPalOrderId: null,
                    AuthorizationId: authorization.Id ?? authorizationId,
                    Status: authorization.Status?.Value,
                    ExpiresAt: ParseDate(authorization.ExpirationTime),
                    Amount: ToGatewayMoney(authorization.Amount),
                    DeclineReason: null,
                    RequiresPayerAction: false);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("reading the payment authorization", typed);
                throw MapApiError("reading the payment authorization", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayAuthorizeResult> ReauthorizeAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("re-authorize payment", async token =>
        {
            try
            {
                var authorization = await Client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = amount.Currency, Value = FormatAmount(amount.Amount) }
                    },
                    prefer: "return=minimal",
                    requestOptions: null,
                    ct: token);

                return new GatewayAuthorizeResult(
                    Success: true,
                    PayPalOrderId: null,
                    AuthorizationId: authorization.Id ?? authorizationId,
                    Status: authorization.Status?.Value,
                    ExpiresAt: ParseDate(authorization.ExpirationTime),
                    Amount: ToGatewayMoney(authorization.Amount) ?? amount,
                    DeclineReason: null,
                    RequiresPayerAction: false);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("re-authorizing the payment", typed);
                throw MapApiError("re-authorizing the payment", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("capture payment", async token =>
        {
            try
            {
                var captured = await Client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: token);

                var status = captured.Status?.Value;
                if (status == "DECLINED" || status == "FAILED")
                {
                    return new GatewayCaptureResult(false, captured.Id, status, null, null, null,
                        "The capture was declined by the card issuer.");
                }

                return new GatewayCaptureResult(
                    Success: true,
                    CaptureId: captured.Id,
                    Status: status,
                    Amount: ToGatewayMoney(captured.Amount) ?? amount,
                    Fee: ToGatewayMoney(captured.SellerReceivableBreakdown?.PaypalFee),
                    NetAmount: ToGatewayMoney(captured.SellerReceivableBreakdown?.NetAmount),
                    DeclineReason: null);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("capturing the payment", typed);
                throw MapApiError("capturing the payment", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayVoidResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("void payment authorization", async token =>
        {
            try
            {
                GatewayVoidResult result;
                try
                {
                    var authorization = await Client.Payments.VoidPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        payPalRequestId: idempotencyKey,
                        prefer: "return=minimal",
                        requestOptions: null,
                        ct: token);

                    result = new GatewayVoidResult(true, authorization?.Status?.Value ?? "VOIDED", null);
                }
                catch (JsonException)
                {
                    // PayPal answers a successful void with 204 No Content; the SDK fails to
                    // deserialize the empty body. Any 2xx means the hold was released.
                    result = new GatewayVoidResult(true, "VOIDED", null);
                }
                return result;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("releasing the held funds", typed);
                throw MapApiError("releasing the held funds", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayRefundResult> RefundAsync(string captureId, GatewayMoney? amount, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("refund payment", async token =>
        {
            try
            {
                var body = amount is null
                    ? null
                    : new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = amount.Currency, Value = FormatAmount(amount.Amount) }
                    };

                var refund = await Client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=minimal",
                    requestOptions: null,
                    ct: token);

                var status = refund.Status?.Value;
                if (status == "FAILED" || status == "CANCELLED")
                {
                    return new GatewayRefundResult(false, refund.Id, status, amount,
                        $"PayPal reported refund status {status}.");
                }

                return new GatewayRefundResult(
                    Success: true,
                    RefundId: refund.Id,
                    Status: status,
                    Amount: ToGatewayMoney(refund.Amount) ?? amount,
                    DeclineReason: null);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed)) throw ClassifyTypedError("refunding the payment", typed);
                throw MapApiError("refunding the payment", ex.Error);
            }
        }, ct);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<GatewayTransaction>();
        var start = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var end = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        return await CallAsync("search PayPal transactions", async token =>
        {
            int page = 1;
            int totalPages = 1;
            do
            {
                try
                {
                    var response = await Client.TransactionSearch.SearchTransactions(
                        startDate: start,
                        endDate: end,
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        fields: "transaction_info",
                        balanceAffectingRecordsOnly: "Y",
                        pageSize: 100,
                        page: page,
                        requestOptions: null,
                        ct: token);

                    if (response.TotalPages.HasValue) totalPages = response.TotalPages.Value;

                    foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null) continue;
                        var initiation = ParseDate(info.TransactionInitiationDate) ?? DateTimeOffset.MinValue;
                        results.Add(new GatewayTransaction(
                            info.TransactionId,
                            info.TransactionStatus ?? "UNKNOWN",
                            ToGatewayMoney(info.TransactionAmount) ?? new GatewayMoney(0m, _settings.Value.Currency ?? "USD"),
                            initiation,
                            info.TransactionEventCode,
                            info.InvoiceId));
                    }
                }
                catch (SdkException<RawError> ex)
                {
                    // PayPal's transaction search index lags live activity; for a range whose
                    // start date it has not indexed yet it answers 404 "Data for the given
                    // start date is not available". That is "no data available yet", not a
                    // failure - report an empty page set.
                    if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                    {
                        break;
                    }
                    throw MapRawError("searching PayPal transactions", ex.Error);
                }
                page++;
            } while (page <= totalPages);

            return (IReadOnlyList<GatewayTransaction>)results;
        }, ct);
    }

    public async Task<GatewayVaultResult> SaveCardAsync(GatewayCard card, string merchantCustomerId, string idempotencyKey, CancellationToken ct)
    {
        return await CallAsync("save card", async token =>
        {
            try
            {
                // PayPal's vault endpoints 500 on merchant_customer_id values containing
                // characters outside [a-zA-Z0-9-_.] in practice (an email address is
                // accepted by the docs' pattern but rejected server-side), so send a
                // deterministic, PII-free id derived from the buyer's identity.
                var wireCustomerId = DeriveMerchantCustomerId(merchantCustomerId);

                var body = new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = wireCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = ToSdkAddress(card.BillingAddress)
                        }
                    }
                };

                var response = await Client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey + "-pt",
                    body: body,
                    requestOptions: null,
                    ct: token);

                if (string.IsNullOrEmpty(response.Id))
                {
                    return new GatewayVaultResult(false, null, null, null, null,
                        "PayPal did not return a vault token for this card.");
                }

                var vaultedCard = response.PaymentSource?.Card;
                return new GatewayVaultResult(
                    Success: true,
                    VaultId: response.Id,
                    Brand: vaultedCard?.Brand?.Value,
                    LastDigits: vaultedCard?.LastDigits,
                    Expiry: vaultedCard?.Expiry,
                    DeclineReason: null);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var typed)) throw ClassifyTypedError("saving the card", typed);
                throw ClassifyApiError("saving the card", ex.Error);
            }
        }, ct);
    }

    public async Task<GatewayVoidResult> DeleteCardAsync(string vaultId, CancellationToken ct)
    {
        return await CallAsync("delete saved card", async token =>
        {
            try
            {
                await Client.Vault.DeletePaymentToken(
                    id: vaultId,
                    requestOptions: null,
                    ct: token);
                return new GatewayVoidResult(true, "DELETED", null);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                // Already gone on the PayPal side counts as deleted.
                if (ex.Error.TryGetRawError(out var raw) && raw != null && raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return new GatewayVoidResult(true, "ALREADY_DELETED", null);
                }
                if (ex.Error.TryGetError1(out var typed)) throw ClassifyTypedError("deleting the saved card", typed);
                throw MapApiError("deleting the saved card", ex.Error);
            }
        }, ct);
    }

    private static string DeriveMerchantCustomerId(string buyerId)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"eshop-{hex.Substring(0, 24)}";
    }

    private CardRequest BuildCardRequest(GatewayAuthorizeRequest request)
    {
        if (request.Card != null)
        {
            return new CardRequest
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = ToSdkAddress(request.Card.BillingAddress)
            };
        }
        if (!string.IsNullOrEmpty(request.VaultTokenId))
        {
            return new CardRequest
            {
                VaultId = request.VaultTokenId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
        }
        throw new PayPalGatewayException("A payment source is required: card details or a saved card token.");
    }

    private static Address? ToSdkAddress(GatewayAddress? address) =>
        address is null
            ? null
            : new Address
            {
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AdminArea1 = address.AdminArea1,
                AdminArea2 = address.AdminArea2,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode
            };

    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (PayPalGatewayException)
        {
            throw;
        }
        catch (PaymentDeclinedException)
        {
            throw;
        }
        catch (JsonException)
        {
            _logger.LogError("PayPal {Operation}: returned a response that could not be processed.", operation);
            throw new PayPalGatewayException(
                $"PayPal {operation}: the response could not be processed. The outcome is unknown - verify the payment state before retrying.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "PayPal {Operation}: gateway unreachable.", operation);
            throw new PayPalGatewayException(
                $"PayPal {operation}: the payment gateway could not be reached. The outcome is unknown - verify the payment state before retrying.", ex);
        }
    }

    private static PayPalGatewayException MapApiError(string operation, ApiError error) =>
        MapRawError(operation, error.TryGetRawError(out var raw) ? raw : null);

    private static PayPalGatewayException MapRawError(string operation, RawError? raw)
    {
        if (raw != null)
        {
            var body = raw.ReadAsString();
            return new PayPalGatewayException(
                $"PayPal {operation} failed with HTTP {(int)raw.StatusCode}: {Truncate(body)}");
        }
        return new PayPalGatewayException($"PayPal {operation} failed: no details were returned.");
    }

    /// <summary>
    /// Reads PayPal's typed error body and distinguishes business rejections (card declined,
    /// payer action required) from genuine gateway failures.
    /// </summary>
    private static Exception ClassifyTypedError(string operation, Error typed) =>
        ClassifyTypedError(operation, typed.Name, typed.Message, typed.DebugId, ExtractIssues(typed.Details));

    private static Exception ClassifyTypedError(string operation, Error1 typed)
    {
        var issues = new List<string?>();
        if (typed.Details != null)
        {
            foreach (var detail in typed.Details)
            {
                issues.Add(detail?.Issue);
            }
        }
        return ClassifyTypedError(operation, typed.Name, typed.Message, typed.DebugId, issues);
    }

    private static IEnumerable<string?> ExtractIssues(IReadOnlyList<ErrorDetails>? details)
    {
        if (details == null) yield break;
        foreach (var detail in details)
        {
            yield return detail?.Issue;
        }
    }

    private static Exception ClassifyTypedError(string operation, string? name, string? message, string? debugId, IEnumerable<string?> issueSource)
    {
        var issues = new List<string> { name ?? string.Empty };
        issues.AddRange(issueSource.Select(i => i ?? string.Empty));
        var joined = string.Join(" ", issues).ToUpperInvariant();

        if (joined.Contains("PAYER_ACTION_REQUIRED") || joined.Contains("3D_SECURE") || joined.Contains("3DS"))
        {
            return new PaymentDeclinedException(
                $"PayPal requires the cardholder to approve this payment in a browser (3-D Secure challenge): {message}");
        }
        if (joined.Contains("DECLINED")
            || joined.Contains("UNPROCESSABLE_ENTITY")
            || joined.Contains("INSUFFICIENT_FUNDS")
            || joined.Contains("EXPIRED_CARD")
            || joined.Contains("INVALID_SECURITY_CODE")
            || joined.Contains("CARD_SECURITY_CODE_MISMATCH"))
        {
            return new PaymentDeclinedException($"The payment was declined: {message}{FormatIssues(issueSource)} ({name})");
        }
            return new PayPalGatewayException(
                $"PayPal {operation} failed: {name} - {message}{FormatIssues(issueSource)} (debug id: {debugId})");
        }

    private static string FormatIssues(IEnumerable<string?> issues)
    {
        var list = issues.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        return list.Count > 0 ? $" [{string.Join(", ", list)}]" : string.Empty;
    }

    /// <summary>
    /// Reads PayPal's error body and distinguishes business rejections (card declined,
    /// payer action required) from genuine gateway failures.
    /// </summary>
    private static Exception ClassifyApiError(string operation, ApiError error)
    {
        var raw = error.TryGetRawError(out var rawError) ? rawError : null;
        if (raw == null)
        {
            return new PayPalGatewayException($"PayPal {operation} failed: no details were returned.");
        }

        var body = raw.ReadAsString();
        var (declined, payerAction, message) = ClassifyErrorBody(body);
        if (payerAction)
        {
            return new PaymentDeclinedException(
                $"PayPal requires the cardholder to approve this payment in a browser (3-D Secure challenge): {message}");
        }
        if (declined)
        {
            return new PaymentDeclinedException($"The payment was declined: {message}");
        }
        return new PayPalGatewayException(
            $"PayPal {operation} failed with HTTP {(int)raw.StatusCode}: {Truncate(body)}");
    }

    private static (bool Declined, bool PayerActionRequired, string Message) ClassifyErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, false, "no details returned");
        }

        var message = body;
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                message = msgEl.GetString() ?? body;
            }
            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                issues.Add(nameEl.GetString() ?? string.Empty);
            }
            if (root.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in detailsEl.EnumerateArray())
                {
                    if (detail.TryGetProperty("issue", out var issueEl) && issueEl.ValueKind == JsonValueKind.String)
                    {
                        issues.Add(issueEl.GetString() ?? string.Empty);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return (false, false, Truncate(body));
        }

        var joined = string.Join(" ", issues).ToUpperInvariant();
        var payerAction = joined.Contains("PAYER_ACTION_REQUIRED") || joined.Contains("3D_SECURE") || joined.Contains("3DS");
        var declined = joined.Contains("DECLINED")
            || joined.Contains("UNPROCESSABLE_ENTITY")
            || joined.Contains("INSUFFICIENT_FUNDS")
            || joined.Contains("EXPIRED_CARD")
            || joined.Contains("INVALID_SECURITY_CODE")
            || joined.Contains("CARD_SECURITY_CODE_MISMATCH");
        return (declined, payerAction, message);
    }

    private static GatewayMoney? ToGatewayMoney(Money? money) =>
        money is null || money.Value is null || money.CurrencyCode is null
            ? null
            : new GatewayMoney(decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture), money.CurrencyCode);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= 500 ? text : text.Substring(0, 500) + "...";
    }
}




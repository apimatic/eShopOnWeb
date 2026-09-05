using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implementation of <see cref="IPaymentGateway"/> on top of the PayPal .NET SDK
/// (<c>PayPalServerSdk</c>). Translates every SDK exception into a classified
/// <see cref="GatewayResult{T}"/> so callers never touch SDK types.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    /// <summary>Bounds every gateway call (the only true whole-call budget).</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    private readonly PayPalServerSdkClient _client;
    private readonly PaymentOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, IOptions<PaymentOptions> options, ILogger<PayPalGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured: set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET).");
        }

        var environment = (_options.Environment ?? "sandbox").Trim().ToLowerInvariant();
        if (environment != "sandbox" && environment != "live")
        {
            throw new InvalidOperationException(
                $"PayPal:Environment '{_options.Environment}' is not recognized; use 'sandbox' or 'live'.");
        }

        // When PayPal:BaseUrl is set it is used verbatim for every call, including the token
        // request. Otherwise the URL is derived from the environment. The SDK's environment
        // selector only models sandbox, so a live target is routed through the sandbox slot
        // with the live base URL.
        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl
            : environment == "live" ? LiveBaseUrl : SandboxBaseUrl;

        var clientOptions = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Server = new ServerOptions
            {
                Default = new DefaultOptions
                {
                    Sandbox = new DefaultOptions.SandboxOptions { BaseUrl = baseUrl }
                }
            },
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(15)
            }
        };

        _client = new PayPalServerSdkClient(httpClient, clientOptions);
    }

    public string Currency => _options.Currency;

    public async Task<GatewayResult<AuthorizeOutcome>> AuthorizeAsync(string requestId, decimal amount, string currency,
        CardInput card, string invoiceId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            var cardRequest = BuildCardRequest(card);
            return await AuthorizeCoreAsync(requestId, amount, currency, cardRequest, invoiceId, token);
        }, ct);
    }

    public async Task<GatewayResult<AuthorizeOutcome>> AuthorizeWithVaultTokenAsync(string requestId, decimal amount,
        string currency, string vaultTokenId, string invoiceId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            var cardRequest = new CardRequest { VaultId = vaultTokenId };
            return await AuthorizeCoreAsync(requestId, amount, currency, cardRequest, invoiceId, token);
        }, ct);
    }

    private async Task<GatewayResult<AuthorizeOutcome>> AuthorizeCoreAsync(string requestId, decimal amount,
        string currency, CardRequest cardRequest, string invoiceId, CancellationToken ct)
    {
        try
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = FormatAmount(amount)
                        },
                        ReferenceId = "default",
                        InvoiceId = invoiceId
                    }
                }
            };

            var paypalOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                ct: ct);

            if (string.IsNullOrEmpty(paypalOrder.Id))
            {
                return Fail<AuthorizeOutcome>(PaymentErrorType.ProviderError, "The provider returned no order id.");
            }

            // Authorize (hold) the order: a valid payment_source authorizes without buyer approval.
            var authorizedOrder = await _client.Orders.AuthorizeOrder(
                id: paypalOrder.Id,
                payPalMockResponse: null,
                payPalRequestId: $"{requestId}-authz",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                },
                ct: ct);

            var authorization = ExtractAuthorization(authorizedOrder);
            _logger.LogInformation("PayPal authorize: order={OrderId} status={Status} units={Units} authFound={Auth}",
                paypalOrder.Id, authorizedOrder.Status?.Value, authorizedOrder.PurchaseUnits?.Count, authorization != null);
            if (authorization == null)
            {
                // The minimal response may omit the payments collection; fetch the full order.
                var fullOrder = await _client.Orders.GetOrder(
                    id: paypalOrder.Id,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct);
                authorization = ExtractAuthorization(fullOrder);
                _logger.LogInformation("PayPal authorize fallback GetOrder: units={Units} authFound={Auth}",
                    fullOrder.PurchaseUnits?.Count, authorization != null);
            }

            if (authorization == null || string.IsNullOrEmpty(authorization.Id))
            {
                return Fail<AuthorizeOutcome>(PaymentErrorType.ProviderError,
                    "The provider did not report an authorization for the order.");
            }

            return GatewayResult<AuthorizeOutcome>.Success(new AuthorizeOutcome
            {
                PayPalOrderId = paypalOrder.Id,
                AuthorizationId = authorization.Id,
                AuthorizationStatus = authorization.Status?.Value ?? string.Empty,
                ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
            });
        }
        catch (SdkException<CreateOrderError> ex)
        {
            return FailFrom<AuthorizeOutcome>(CreateOrderLadder(ex), default);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            return FailFrom<AuthorizeOutcome>(AuthorizeOrderLadder(ex), null);
        }
        catch (SdkException<GetOrderError> ex)
        {
            return FailFrom<AuthorizeOutcome>(GetOrderLadder(ex), null);
        }
        catch (SdkException<RawError> ex)
        {
            return FailFromRaw<AuthorizeOutcome>(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return FailFromTransport<AuthorizeOutcome>(ex);
        }
        catch (JsonException ex)
        {
            return Fail<AuthorizeOutcome>(PaymentErrorType.ProviderError,
                "The provider returned a response that could not be processed.", ex);
        }
    }

    public async Task<GatewayResult<ReauthorizeOutcome>> ReauthorizeAsync(string requestId, string authorizationId,
        decimal amount, string currency, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var authorization = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                    },
                    ct: token);

                return GatewayResult<ReauthorizeOutcome>.Success(new ReauthorizeOutcome
                {
                    AuthorizationId = authorization.Id ?? authorizationId,
                    AuthorizationStatus = authorization.Status?.Value ?? string.Empty,
                    ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
                });
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                var raw = ex.Error.TryGetRawError(out var rawError) ? rawError : null;
                return FailFrom<ReauthorizeOutcome>(ReauthorizeLadder(ex), raw);
            }
            catch (SdkException<RawError> raw)
            {
                return FailFromRaw<ReauthorizeOutcome>(raw);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<ReauthorizeOutcome>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<ReauthorizeOutcome>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<AuthorizationInfo>> GetAuthorizationAsync(string authorizationId,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var authorization = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);

                return GatewayResult<AuthorizationInfo>.Success(new AuthorizationInfo
                {
                    AuthorizationId = authorization.Id ?? authorizationId,
                    AuthorizationStatus = authorization.Status?.Value ?? string.Empty,
                    ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
                });
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                RawError? raw = null;
                if (ex.Error.TryGetNoContent(out var noContent)) raw = noContent;
                else if (ex.Error.TryGetRawError(out var rawError)) raw = rawError;
                if (ex.Error.TryGetError(out var error))
                {
                    var issues = IssueList(error);
                    var message = DescribeError(error);
                    var type = Classify(message + " " + string.Join(" ", issues));
                    return Fail<AuthorizationInfo>(type, message, null, issues);
                }
                var statusCode = raw != null ? (int)raw.StatusCode : 0;
                return Fail<AuthorizationInfo>(statusCode == 404 ? PaymentErrorType.NotFound : PaymentErrorType.ProviderError,
                    $"The provider returned HTTP {statusCode} fetching the authorization.", null,
                    new List<string> { RawBody(raw) });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<AuthorizationInfo>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<AuthorizationInfo>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<CaptureOutcome>> CaptureAsync(string requestId, string authorizationId,
        decimal amount, string currency, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                        FinalCapture = true
                    },
                    ct: token);

                var breakdown = captured.SellerReceivableBreakdown;
                if (breakdown == null && !string.IsNullOrEmpty(captured.Id))
                {
                    // The capture response may omit the seller receivable breakdown (fee/net);
                    // re-fetch the capture, which returns the same record with the breakdown.
                    try
                    {
                        var fetched = await _client.Payments.GetCapturedPayment(
                            captureId: captured.Id,
                            payPalMockResponse: null,
                            ct: token);
                        breakdown = fetched.SellerReceivableBreakdown;
                        if (captured.Amount == null)
                        {
                            captured = fetched;
                        }
                    }
                    catch (SdkException<GetCapturedPaymentError>)
                    {
                        // Fee/net stay unknown; they remain null on the payment record.
                    }
                }

                return GatewayResult<CaptureOutcome>.Success(new CaptureOutcome
                {
                    CaptureId = captured.Id ?? string.Empty,
                    CaptureStatus = captured.Status?.Value ?? string.Empty,
                    CapturedAmount = ParseAmount(captured.Amount?.Value) ?? amount,
                    Currency = captured.Amount?.CurrencyCode ?? currency,
                    PayPalFee = ParseAmount(breakdown?.PaypalFee?.Value),
                    NetAmount = ParseAmount(breakdown?.NetAmount?.Value)
                });
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                return FailFrom<CaptureOutcome>(CaptureLadder(ex), null);
            }
            catch (SdkException<RawError> raw)
            {
                return FailFromRaw<CaptureOutcome>(raw);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<CaptureOutcome>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<CaptureOutcome>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<string>> VoidAsync(string requestId, string authorizationId,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var authorization = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    ct: token);

                return GatewayResult<string>.Success(authorization.Status?.Value ?? "VOIDED");
            }
            catch (JsonException)
            {
                // A successful void returns an EMPTY body, which the SDK fails to deserialize
                // into PaymentAuthorization. Confirm the void actually took effect by
                // re-reading the authorization.
                try
                {
                    var confirmation = await _client.Payments.GetAuthorizedPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        ct: token);
                    var status = confirmation.Status?.Value ?? string.Empty;
                    if (status == "VOIDED")
                    {
                        return GatewayResult<string>.Success(status);
                    }

                    return GatewayResult<string>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                        $"The provider returned an unreadable response to the void; the authorization is {status}."));
                }
                catch (Exception readEx)
                {
                    _logger.LogError(readEx, "PayPal void: unreadable response and the authorization could not be re-read.");
                    return GatewayResult<string>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                        "The provider returned an unreadable response to the void and the hold state is unknown."));
                }
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                return FailFrom<string>(VoidLadder(ex), null);
            }
            catch (SdkException<RawError> raw)
            {
                return FailFromRaw<string>(raw);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<string>(ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<RefundOutcome>> RefundAsync(string requestId, string captureId, decimal? amount,
        string currency, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                // A full refund is an empty payload; a partial refund carries the amount.
                RefundRequest? body = amount.HasValue
                    ? new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) }
                    }
                    : null;

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    ct: token);

                return GatewayResult<RefundOutcome>.Success(new RefundOutcome
                {
                    RefundId = refund.Id ?? string.Empty,
                    Status = refund.Status?.Value ?? string.Empty,
                    Amount = ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
                    Currency = refund.Amount?.CurrencyCode ?? currency,
                    TotalRefundedAmount = ParseAmount(refund.SellerPayableBreakdown?.TotalRefundedAmount?.Value)
                });
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                return FailFrom<RefundOutcome>(RefundLadder(ex), null);
            }
            catch (SdkException<RawError> raw)
            {
                return FailFromRaw<RefundOutcome>(raw);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<RefundOutcome>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<RefundOutcome>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<VaultOutcome>> VaultCardAsync(string buyerId, CardInput card,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                var tokenResponse = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: $"eshop-vault-{Guid.NewGuid():N}",
                    body: new PaymentTokenRequest
                    {
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Name = card.Name,
                                Number = card.Number,
                                Expiry = FormatExpiry(card),
                                SecurityCode = card.SecurityCode,
                                BillingAddress = BuildAddress(card.BillingAddress)
                            }
                        }
                        // No Customer block: sending merchant_customer_id makes the sandbox
                        // respond 500; without it PayPal vaults the card and assigns its own
                        // customer id, which we persist from the response.
                    },
                    ct: token);
                _logger.LogInformation("PayPal vault: token={TokenId} brand={Brand} last4={Last4}",
                    tokenResponse.Id, tokenResponse.PaymentSource?.Card?.Brand?.Value,
                    tokenResponse.PaymentSource?.Card?.LastDigits);

                var cardEntity = tokenResponse.PaymentSource?.Card;
                return GatewayResult<VaultOutcome>.Success(new VaultOutcome
                {
                    TokenId = tokenResponse.Id ?? string.Empty,
                    CustomerId = tokenResponse.Customer?.Id,
                    Brand = cardEntity?.Brand?.Value ?? string.Empty,
                    LastDigits = cardEntity?.LastDigits ?? string.Empty,
                    Expiry = cardEntity?.Expiry,
                    CardholderName = cardEntity?.Name
                });
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                return FailFrom<VaultOutcome>(VaultLadder(ex), null);
            }
            catch (SdkException<RawError> raw)
            {
                return FailFromRaw<VaultOutcome>(raw);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<VaultOutcome>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<VaultOutcome>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<bool>> DeleteVaultTokenAsync(string vaultTokenId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(
                    id: vaultTokenId,
                    ct: token);
                return GatewayResult<bool>.Success(true);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                RawError? raw = null;
                if (ex.Error.TryGetError1(out var error1))
                {
                    var message = DescribeError1(error1);
                    return Fail<bool>(Classify(message), message, null, IssueList1(error1));
                }
                if (ex.Error.TryGetRawError(out var rawError)) raw = rawError;
                var statusCode = raw != null ? (int)raw.StatusCode : 0;
                return Fail<bool>(statusCode == 404 ? PaymentErrorType.NotFound : PaymentErrorType.ProviderError,
                    $"The provider returned HTTP {statusCode} deleting the payment token.", null,
                    new List<string> { RawBody(raw) });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return FailFromTransport<bool>(ex);
            }
            catch (JsonException ex)
            {
                return Fail<bool>(PaymentErrorType.ProviderError,
                    "The provider returned a response that could not be processed.", ex);
            }
        }, ct);
    }

    public async Task<GatewayResult<ReconciliationResult>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        return await ExecuteAsync(async token =>
        {
            // PayPal's transaction search is paginated by page number: walk every page so the
            // whole range is covered, not just the first page of it.
            var transactions = new List<ReconciliationTransaction>();
            string? lastRefreshed = null;
            var page = 1;
            const int pageSize = 100;
            const int maxPages = 500;

            while (page <= maxPages)
            {
                var response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatUtcDate(from),
                    endDate: FormatUtcDate(to),
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
                    pageSize: pageSize,
                    page: page,
                    ct: token);

                lastRefreshed = response.LastRefreshedDatetime ?? lastRefreshed;
                var details = response.TransactionDetails ?? new List<TransactionDetails>();
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info == null) continue;
                    transactions.Add(new ReconciliationTransaction
                    {
                        TransactionId = info.TransactionId ?? string.Empty,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = ParseAmount(info.TransactionAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        FeeAmount = ParseAmount(info.FeeAmount?.Value),
                        InitiationDate = ParseTimestamp(info.TransactionInitiationDate),
                        InvoiceId = info.InvoiceId,
                        ReferenceId = info.PaypalReferenceId
                    });
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || details.Count == 0)
                {
                    break;
                }

                page++;
            }

            return GatewayResult<ReconciliationResult>.Success(new ReconciliationResult
            {
                Transactions = transactions,
                LastRefreshedDatetime = lastRefreshed
            });
        }, ct);
    }

    // ---- order/authorization plumbing ---------------------------------------------------------

    private static AuthorizationWithAdditionalData? ExtractAuthorization(Order order)
    {
        var unit = order.PurchaseUnits?.FirstOrDefault();
        return unit?.Payments?.Authorizations?.FirstOrDefault();
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(OrderAuthorizeResponse response)
    {
        var unit = response.PurchaseUnits?.FirstOrDefault();
        return unit?.Payments?.Authorizations?.FirstOrDefault();
    }

    private static CardRequest BuildCardRequest(CardInput card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = FormatExpiry(card),
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address BuildAddress(BillingAddressInput? input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.CountryCode))
        {
            throw new InvalidOperationException("A billing address with a country code is required for card payments.");
        }

        return new Address
        {
            CountryCode = input.CountryCode,
            AddressLine1 = input.AddressLine1,
            AddressLine2 = input.AddressLine2,
            AdminArea1 = input.AdminArea1,
            AdminArea2 = input.AdminArea2,
            PostalCode = input.PostalCode
        };
    }

    private static string FormatExpiry(CardInput card) =>
        $"{card.ExpiryYear:0000}-{card.ExpiryMonth:00}";

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatUtcDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // ---- error translation ---------------------------------------------------------------------

    private delegate void UnusedDelegate();

    private static List<string> IssueList(Error error) =>
        error.Details?.Select(d => d.Issue).ToList() ?? new List<string>();

    private static List<string> IssueList1(Error1 error) =>
        error.Details?.Select(d => d.Issue).ToList() ?? new List<string>();

    private static string DescribeError(Error error) =>
        $"[{error.Name}] {error.Message}";

    private static string DescribeError1(Error1 error) =>
        $"[{error.Name}] {error.Message}";

    private static string RawBody(RawError? raw)
    {
        if (raw == null) return string.Empty;
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeRead(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static PaymentErrorType Classify(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("not_authorized") || lower.Contains("permission_denied") ||
            lower.Contains("insufficient permissions"))
        {
            return PaymentErrorType.Forbidden;
        }

        if (lower.Contains("instrument_declined") || lower.Contains("card_declined") || lower.Contains("declined") ||
            lower.Contains("insufficient_funds") || lower.Contains("invalid_card") || lower.Contains("cc_reject") ||
            lower.Contains("transaction_refused"))
        {
            return PaymentErrorType.Declined;
        }

        if (lower.Contains("authorization_expired") || lower.Contains("authorization_voided") ||
            lower.Contains("authorization_already") || lower.Contains("not_renewable") ||
            lower.Contains("authorization_id_does_not_exist"))
        {
            return PaymentErrorType.StaleAuthorization;
        }

        if (lower.Contains("invalid_resource_id") || lower.Contains("resource_not_found") ||
            lower.Contains("does not exist") || lower.Contains("not_found"))
        {
            return PaymentErrorType.NotFound;
        }

        return PaymentErrorType.ProviderError;
    }

    private static GatewayResult<T> Fail<T>(PaymentErrorType type, string message, Exception? ex = null,
        List<string>? issues = null)
    {
        var fullMessage = issues is { Count: > 0 } ? $"{message} ({string.Join("; ", issues)})" : message;
        return GatewayResult<T>.Failure(new PaymentError(type, fullMessage));
    }

    private static GatewayResult<T> FailFrom<T>(List<string> issues, RawError? raw)
    {
        var text = string.Join("; ", issues);
        var type = Classify(text);
        if (raw != null && type == PaymentErrorType.ProviderError)
        {
            var statusCode = (int)raw.StatusCode;
            var body = RawBody(raw);
            return GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                $"The provider returned HTTP {statusCode}. {Truncate(body)}"));
        }

        return GatewayResult<T>.Failure(new PaymentError(type, $"The provider rejected the request. {text}"));
    }

    private static GatewayResult<T> FailFromRaw<T>(SdkException<RawError> ex)
    {
        var body = SafeRead(ex.Error);
        var type = Classify(body);
        var statusCode = (int)ex.Error.StatusCode;
        if (type == PaymentErrorType.ProviderError)
        {
            return GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                $"The provider returned HTTP {statusCode}. {Truncate(body)}"));
        }

        return GatewayResult<T>.Failure(new PaymentError(type, $"The provider rejected the request. {Truncate(body)}"));
    }

    private static GatewayResult<T> FailFromTransport<T>(Exception ex) =>
        GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.TransportFailure,
            "The payment provider could not be reached."));

    private static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 500) return value ?? string.Empty;
        return value[..500];
    }

    // Per-operation error ladders: one branch per public TryGet* accessor on the operation's
    // error type, TryGetRawError last (it only fires for statuses without a typed accessor).

    private static List<string> CreateOrderLadder(SdkException<CreateOrderError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> AuthorizeOrderLadder(SdkException<AuthorizeOrderError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> GetOrderLadder(SdkException<GetOrderError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> CaptureLadder(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            issues.Add(RawBody(noContent));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> ReauthorizeLadder(SdkException<ReauthorizePaymentError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            issues.Add(RawBody(noContent));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> VoidLadder(SdkException<VoidPaymentError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            issues.Add(RawBody(noContent));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> RefundLadder(SdkException<RefundCapturedPaymentError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError(out var error))
        {
            issues.Add(DescribeError(error));
            issues.AddRange(IssueList(error));
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            issues.Add(RawBody(noContent));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    private static List<string> VaultLadder(SdkException<CreatePaymentTokenError> ex)
    {
        var issues = new List<string>();
        if (ex.Error.TryGetError1(out var error1))
        {
            issues.Add(DescribeError1(error1));
            issues.AddRange(IssueList1(error1));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            issues.Add(RawBody(raw));
        }
        return issues;
    }

    // ---- shared execution wrapper ---------------------------------------------------------------

    /// <summary>
    /// Bounds a gateway call with a whole-call budget and normalizes Case B errors (raw),
    /// connection failures and unreadable bodies into the same result shape as API errors.
    /// Case A operation errors are caught inside each call site, where the concrete error
    /// type and its typed accessors are known.
    /// </summary>
    private async Task<GatewayResult<T>> ExecuteAsync<T>(Func<CancellationToken, Task<GatewayResult<T>>> call,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var body = SafeRead(ex.Error);
            var type = Classify(body);
            if (type == PaymentErrorType.ProviderError)
            {
                return GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                    $"The provider returned HTTP {(int)ex.Error.StatusCode}. {Truncate(body)}"));
            }

            return GatewayResult<T>.Failure(new PaymentError(type, $"The provider rejected the request. {Truncate(body)}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Payment gateway transport failure: {Msg}", ex.Message);
            return GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.TransportFailure,
                "The payment provider could not be reached."));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Payment gateway returned an unreadable response: {Msg}", ex.Message);
            return GatewayResult<T>.Failure(new PaymentError(PaymentErrorType.ProviderError,
                "The provider returned a response that could not be processed."));
        }
    }
}












using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using GatewayModels = Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single seam that talks to the PayPal Server SDK. Maps provider-neutral gateway models onto SDK
/// models and translates every SDK failure into <see cref="PayPalGatewayException"/>.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    // PayPal Transaction Search limits a single query to a 31-day range.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalOptions> options,
        IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency;

    public async Task<string> CreateOrderAsync(
        string reference, GatewayModels.GatewayMoney amount, string invoiceId, string customId, string description, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = amount.CurrencyCode, Value = amount.Value },
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Description = Truncate(description, 127),
                },
            },
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: reference,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                ct: ct);

            return order.Id ?? throw new PayPalGatewayException("PayPal did not return an order id.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<AuthorizationResult> AuthorizeOrderAsync(
        string reference, string payPalOrderId, CardDetails? card, string? vaultId, CancellationToken ct)
    {
        OrderAuthorizeRequest? body = null;
        if (card is not null)
        {
            body = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = BuildCard(card) },
            };
        }
        else if (!string.IsNullOrEmpty(vaultId))
        {
            body = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { VaultId = vaultId } },
            };
        }

        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: reference,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var authorization = FirstAuthorization(response.PurchaseUnits);
            if (authorization is null || string.IsNullOrEmpty(authorization.Id))
            {
                var payerAction = response.Status is not null && response.Status == OrderStatus.PayerActionRequired;
                throw new PayPalGatewayException(
                    payerAction
                        ? "PayPal requires the shopper to approve this card payment in a browser (challenge/3-D Secure). This integration does not perform browser approval."
                        : $"PayPal returned no authorization for the order (status: {response.Status?.Value ?? "unknown"}).")
                {
                    IsPayerActionRequired = payerAction,
                };
            }

            return new AuthorizationResult(
                payPalOrderId,
                authorization.Id!,
                authorization.Status?.Value ?? "UNKNOWN",
                ToGatewayMoney(authorization.Amount));
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<AuthorizationResult?> TryReadOrderAuthorizationAsync(string payPalOrderId, CancellationToken ct)
    {
        try
        {
            var order = await _client.Orders.GetOrder(
                id: payPalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            var authorization = FirstAuthorization(order.PurchaseUnits);
            if (authorization is null || string.IsNullOrEmpty(authorization.Id))
            {
                return null;
            }

            return new AuthorizationResult(
                payPalOrderId,
                authorization.Id!,
                authorization.Status?.Value ?? "UNKNOWN",
                ToGatewayMoney(authorization.Amount));
        }
        catch (SdkException<GetOrderError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string reference, string authorizationId, GatewayModels.GatewayMoney amount, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = amount.CurrencyCode, Value = amount.Value },
            FinalCapture = true,
        };

        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: reference,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            return new CaptureResult(
                capture.Id ?? throw new PayPalGatewayException("PayPal did not return a capture id."),
                capture.Status?.Value ?? "UNKNOWN",
                ToGatewayMoney(breakdown?.GrossAmount) ?? ToGatewayMoney(capture.Amount),
                ToGatewayMoney(breakdown?.PaypalFee),
                ToGatewayMoney(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var error);
            RawError? raw = null;
            if (error is null && !ex.Error.TryGetNoContent(out raw))
            {
                ex.Error.TryGetRawError(out raw);
            }

            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(
        string reference, string authorizationId, GatewayModels.GatewayMoney amount, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = amount.CurrencyCode, Value = amount.Value },
        };

        try
        {
            var result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: reference,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new ReauthorizeResult(
                result.Id ?? throw new PayPalGatewayException("PayPal did not return a reauthorization id."),
                result.Status?.Value ?? "UNKNOWN");
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var error);
            RawError? raw = null;
            if (error is null && !ex.Error.TryGetNoContent(out raw))
            {
                ex.Error.TryGetRawError(out raw);
            }

            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task VoidAsync(string reference, string authorizationId, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: reference,
                ct: ct);
        }
        catch (System.Text.Json.JsonException)
        {
            // VoidPayment succeeds with 204 No Content; the SDK throws while deserializing the empty
            // body into PaymentAuthorization. An empty 2xx body here means the void was accepted.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            ex.Error.TryGetError(out var error);
            RawError? raw = null;
            if (error is null && !ex.Error.TryGetNoContent(out raw))
            {
                ex.Error.TryGetRawError(out raw);
            }

            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw Wrap(ex);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string idempotencyKey, string captureId, GatewayModels.GatewayMoney amount, string customId, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = new Money { CurrencyCode = amount.CurrencyCode, Value = amount.Value },
            CustomId = customId,
        };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new RefundResult(
                refund.Id ?? throw new PayPalGatewayException("PayPal did not return a refund id."),
                refund.Status?.Value ?? "UNKNOWN",
                ToGatewayMoney(refund.Amount));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var error);
            RawError? raw = null;
            if (error is null && !ex.Error.TryGetNoContent(out raw))
            {
                ex.Error.TryGetRawError(out raw);
            }

            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<VaultedCard> VaultCardAsync(
        string reference, CardDetails card, string merchantCustomerId, CancellationToken ct)
    {
        try
        {
            var setupBody = new SetupTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new SetupTokenRequestPaymentSource
                {
                    Card = new SetupTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = BuildAddress(card.BillingAddress),
                    },
                },
            };

            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: $"{reference}-setup",
                body: setupBody,
                ct: ct);

            var setupId = setup.Id ?? throw new PayPalGatewayException("PayPal did not return a setup token id.");

            var tokenBody = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken },
                },
            };

            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"{reference}-token",
                body: tokenBody,
                ct: ct);

            var vaultId = token.Id ?? throw new PayPalGatewayException("PayPal did not return a vault (payment token) id.");
            var cardInfo = token.PaymentSource?.Card;

            return new VaultedCard(
                vaultId,
                cardInfo?.Brand?.Value,
                cardInfo?.LastDigits,
                cardInfo?.Expiry,
                cardInfo?.Name);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            ex.Error.TryGetError(out var error);
            ex.Error.TryGetRawError(out var raw);
            throw Fail(ex, error, raw);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw Wrap(ex);
        }
    }

    public async Task<ReconciliationResult> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, int maxPages, CancellationToken ct)
    {
        var transactions = new List<ReconciliationTransaction>();
        var pagesScanned = 0;
        var truncated = false;
        int? totalItems = 0;

        // PayPal limits a query to 31 days, so walk the range in <=31-day windows, paging each.
        var windowStart = from;
        while (windowStart < to && !truncated)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(windowStart),
                        endDate: FormatSearchDate(windowEnd),
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
                        pageSize: SearchPageSize,
                        page: page,
                        ct: ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw Fail(ex, null, ex.Error);
                }
                catch (Exception ex) when (IsInfrastructureFailure(ex))
                {
                    throw Wrap(ex);
                }

                pagesScanned++;
                totalPages = response.TotalPages ?? 1;
                totalItems = (totalItems ?? 0) + (response.TransactionDetails?.Count ?? 0);

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    transactions.Add(new ReconciliationTransaction(
                        info?.TransactionId ?? string.Empty,
                        info?.TransactionStatus,
                        info?.TransactionAmount?.CurrencyCode,
                        info?.TransactionAmount?.Value,
                        info?.TransactionInitiationDate,
                        info?.InvoiceId,
                        info?.CustomField));
                }

                if (pagesScanned >= maxPages && page < totalPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation truncated at {Pages} pages; more transactions exist for the range.", pagesScanned);
                    break;
                }

                page++;
            }
            while (page <= totalPages && !ct.IsCancellationRequested);

            windowStart = windowEnd;
        }

        return new ReconciliationResult(transactions, pagesScanned, totalItems, truncated);
    }

    // ---- mapping helpers ----

    private static CardRequest BuildCard(CardDetails card) => new CardRequest
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card.BillingAddress),
    };

    private static SdkAddress? BuildAddress(GatewayAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new SdkAddress
        {
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            // country_code is required by the SDK model; default to US when a caller omits it.
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode!,
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static GatewayModels.GatewayMoney? ToGatewayMoney(Money? money) =>
        money is null ? null : new GatewayModels.GatewayMoney(money.CurrencyCode, money.Value);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    // ---- error translation ----

    private PayPalGatewayException Fail(Exception inner, Error? error, RawError? raw)
    {
        int? status = raw is not null ? (int)raw.StatusCode : null;
        var name = error?.Name;
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var debugId = error?.DebugId;
        var providerMessage = error?.Message;

        var expired = ContainsToken(issue, "EXPIRED") || ContainsToken(name, "EXPIRED");
        var payerAction = ContainsToken(issue, "PAYER_ACTION") || ContainsToken(name, "PAYER_ACTION")
            || ContainsToken(issue, "3D") && ContainsToken(issue, "SECURE");

        var message = BuildSafeMessage(status, name, issue, providerMessage, debugId);
        _logger.LogWarning("PayPal call failed: {Message}", message);

        return new PayPalGatewayException(message, inner)
        {
            StatusCode = status,
            ErrorName = name,
            Issue = issue,
            DebugId = debugId,
            IsAuthorizationExpired = expired,
            IsPayerActionRequired = payerAction,
        };
    }

    private static PayPalGatewayException Wrap(Exception ex) => ex switch
    {
        System.Text.Json.JsonException => new PayPalGatewayException(
            "PayPal returned a response that could not be processed.", ex),
        _ => new PayPalGatewayException("PayPal is currently unreachable. Please try again.", ex),
    };

    private static bool IsInfrastructureFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Text.Json.JsonException;

    private static string BuildSafeMessage(int? status, string? name, string? issue, string? providerMessage, string? debugId)
    {
        var parts = new List<string>();
        parts.Add(status is not null ? $"PayPal returned HTTP {status}" : "PayPal returned an error");
        if (!string.IsNullOrEmpty(name))
        {
            parts.Add(name!);
        }

        if (!string.IsNullOrEmpty(issue))
        {
            parts.Add(issue!);
        }

        if (!string.IsNullOrEmpty(providerMessage))
        {
            parts.Add(providerMessage!);
        }

        if (!string.IsNullOrEmpty(debugId))
        {
            parts.Add($"(debug_id: {debugId})");
        }

        return string.Join(" — ", parts);
    }

    private static bool ContainsToken(string? value, string token) =>
        value is not null && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}

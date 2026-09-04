using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using ErrorModel = PayPalServerSdk.Models.Error;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only place the PayPal SDK is touched. Every operation: a bounded call budget,
/// a single-send guard on payment writes (transport retries can never duplicate a hold,
/// capture or refund), and the full generated catch ladder per operation, translated into
/// one PaymentGatewayException with a caller-safe message (no provider internals,
/// no echoed request values, no card data — there is none on these bodies anyway).
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------- authorize ----------------

    public async Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct = default)
    {
        return await RunWriteAsync("authorize", ct, async token =>
        {
            var created = await CreateCheckoutOrderAsync(request, token);
            var providerOrderId = created;

            try
            {
                return await AuthorizeProviderOrderAsync(providerOrderId, request.Source, token);
            }
            catch (PaymentGatewayException ex) when (ex is { Kind: not PaymentFailureKind.ProtocolError, ProviderOrderId: null })
            {
                // The hold failed on a known provider order: remember which order so a replay
                // completes THAT hold instead of creating a second one.
                ex.ProviderOrderId = providerOrderId;
                throw;
            }
        });
    }

    private async Task<string> CreateCheckoutOrderAsync(GatewayAuthorizeRequest request, CancellationToken ct)
    {
        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = request.Currency,
                                Value = MoneyValue(request.Amount)
                            },
                            InvoiceId = request.InvoiceReference,
                            CustomId = string.IsNullOrEmpty(request.CustomReference) ? request.InvoiceReference : request.CustomReference
                        }
                    }
                },
                ct: ct);

            return order.Id ?? throw Protocol("authorize", "the provider order response carried no id.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw FromTypedError("authorize", error);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("authorize", raw);
            }
            throw Protocol("authorize", "the provider error body could not be read.");
        }
    }

    public async Task<GatewayAuthorization> AuthorizeExistingOrderAsync(string providerOrderId, GatewayAuthorizeSource source, CancellationToken ct = default)
    {
        return await RunWriteAsync("authorize-existing-order", ct, async token =>
            await AuthorizeProviderOrderAsync(providerOrderId, source, token));
    }

    private async Task<GatewayAuthorization> AuthorizeProviderOrderAsync(string providerOrderId, GatewayAuthorizeSource source, CancellationToken ct)
    {
        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: providerOrderId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = BuildCardRequest(source) }
                },
                ct: ct);

            // Direct card payments must complete server-side. If PayPal ever demands a
            // browser approval step, surface it as an explicit refusal — no silent round-trip.
            if (response.Status?.Value == "PAYER_ACTION_REQUIRED")
            {
                throw new PaymentGatewayException(
                    PaymentFailureKind.ProviderRejected,
                    "PayPal requires the shopper to approve this payment in a browser; this integration does not support interactive approvals.");
            }

            var authorization = response.PurchaseUnits?
                .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault()
                ?? throw Protocol("authorize", "the provider did not return an authorization on the order.");

            return new GatewayAuthorization(
                AuthorizationId: authorization.Id ?? throw Protocol("authorize", "authorization response carried no id."),
                Status: authorization.Status?.Value ?? "UNKNOWN",
                Amount: ToDecimal(authorization.Amount, "authorize"),
                Currency: authorization.Amount?.CurrencyCode ?? string.Empty,
                ExpirationTime: ParseTimestamp(authorization.ExpirationTime),
                CreatedTime: DateTimeOffset.UtcNow,
                NetworkTransactionReference: authorization.NetworkTransactionReference?.Id,
                ProviderOrderId: response.Id ?? providerOrderId);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw FromTypedError("authorize", error);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError("authorize", raw);
            }
            throw Protocol("authorize", "the provider error body could not be read.");
        }
    }

    private static CardRequest BuildCardRequest(GatewayAuthorizeSource source)
    {
        if (source.Card is not null)
        {
            return new CardRequest
            {
                Number = source.Card.Number,
                Expiry = WireExpiry(source.Card.Expiry),
                SecurityCode = source.Card.SecurityCode,
                Name = source.Card.CardholderName,
                BillingAddress = source.Card.BillingAddress is { } billing
                    ? new Address { CountryCode = billing.CountryCode }
                    : null
            };
        }

        if (source.VaultTokenId is null)
        {
            throw new InvalidOperationException("An authorize call needs either a card or a vault token.");
        }

        return new CardRequest
        {
            VaultId = source.VaultTokenId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                Usage = StoredPaymentSourceUsageType.Derived,
                PreviousNetworkTransactionReference = source.PreviousNetworkTransactionReference is { Length: > 0 } previousNtr
                    ? new NetworkTransaction { Id = previousNtr }
                    : null
            }
        };
    }

    // ---------------- capture / inspect / re-authorize / void ----------------

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        return await RunWriteAsync("capture", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount) },
                        FinalCapture = true
                    },
                    // The SDK default "return=minimal" makes PayPal answer 204 with no body;
                    // the capture result (id, status, fee/net) must be requested explicitly.
                    prefer: "return=representation",
                    ct: token);

                return MapCapturedPayment(response, authorizationId, currency);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw FromPaymentsError("capture", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        return await RunReadAsync("get-authorization", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);

                return new GatewayAuthorization(
                    AuthorizationId: response.Id ?? authorizationId,
                    Status: response.Status?.Value ?? "UNKNOWN",
                    Amount: ToDecimal(response.Amount, "get-authorization"),
                    Currency: response.Amount?.CurrencyCode ?? string.Empty,
                    ExpirationTime: ParseTimestamp(response.ExpirationTime),
                    CreatedTime: ParseTimestamp(response.CreateTime),
                    NetworkTransactionReference: response.NetworkTransactionReference?.Id,
                    ProviderOrderId: response.SupplementaryData?.RelatedIds?.OrderId);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw FromPaymentsError("get-authorization", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        return await RunWriteAsync("reauthorize", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount) }
                    },
                    prefer: "return=representation",
                    ct: token);

                return new GatewayAuthorization(
                    AuthorizationId: response.Id ?? throw Protocol("reauthorize", "the provider returned no authorization id."),
                    Status: response.Status?.Value ?? "UNKNOWN",
                    Amount: ToDecimal(response.Amount, "reauthorize"),
                    Currency: response.Amount?.CurrencyCode ?? currency,
                    ExpirationTime: ParseTimestamp(response.ExpirationTime),
                    CreatedTime: ParseTimestamp(response.CreateTime),
                    NetworkTransactionReference: response.NetworkTransactionReference?.Id,
                    ProviderOrderId: response.SupplementaryData?.RelatedIds?.OrderId);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw FromPaymentsError("reauthorize", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        await RunWriteAsync<object?>("void", ct, async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: null,
                    prefer: "return=representation",
                    ct: token);
                return null;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw FromPaymentsError("void", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    public async Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken ct = default)
    {
        return await RunReadAsync("get-capture", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    ct: token);

                return MapCapturedPayment(response, authorizationId: null, fallbackCurrency: null);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw FromPaymentsError("get-capture", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    private GatewayCapture MapCapturedPayment(CapturedPayment response, string? authorizationId, string? fallbackCurrency)
    {
        var breakdown = response.SellerReceivableBreakdown;
        var gross = breakdown?.GrossAmount is { } grossMoney ? ToDecimal(grossMoney, "capture") : ToDecimal(response.Amount, "capture");
        var fee = breakdown?.PaypalFee is { } feeMoney ? TryToDecimal(feeMoney.Value) : null;
        var net = breakdown?.NetAmount is { } netMoney ? TryToDecimal(netMoney.Value) : null;

        return new GatewayCapture(
            CaptureId: response.Id ?? throw Protocol("capture", "the provider returned a capture without an id."),
            Status: response.Status?.Value ?? "UNKNOWN",
            StatusReason: response.StatusDetails?.Reason?.Value,
            GrossAmount: gross,
            FeeAmount: fee,
            NetAmount: net,
            Currency: response.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? fallbackCurrency ?? string.Empty,
            AuthorizationId: response.SupplementaryData?.RelatedIds?.AuthorizationId ?? authorizationId,
            ProviderOrderId: response.SupplementaryData?.RelatedIds?.OrderId);
    }

    // ---------------- refunds ----------------

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency, string? invoiceReference, CancellationToken ct = default)
    {
        return await RunWriteAsync("refund", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = currency, Value = MoneyValue(amount) },
                        InvoiceId = invoiceReference
                    },
                    prefer: "return=representation",
                    ct: token);

                return MapRefund(response, captureId);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw FromPaymentsError("refund", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    public async Task<GatewayRefund> GetRefundAsync(string refundId, CancellationToken ct = default)
    {
        return await RunReadAsync("get-refund", ct, async token =>
        {
            try
            {
                var response = await _client.Payments.GetRefund(
                    refundId: refundId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);

                return MapRefund(response, captureId: null);
            }
            catch (SdkException<GetRefundError> ex)
            {
                throw FromPaymentsError("get-refund", ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetNoContent(out var nc) ? nc : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    private GatewayRefund MapRefund(Refund response, string? captureId)
    {
        var amount = ToDecimal(response.Amount, "refund");
        var totalRefunded = response.SellerPayableBreakdown?.TotalRefundedAmount is { } totalMoney ? TryToDecimal(totalMoney.Value) : null;

        return new GatewayRefund(
            RefundId: response.Id ?? throw Protocol("refund", "the provider returned a refund without an id."),
            Status: response.Status?.Value ?? "UNKNOWN",
            Amount: amount,
            Currency: response.Amount?.CurrencyCode ?? string.Empty,
            TotalRefundedAmount: totalRefunded,
            CaptureId: captureId,
            ProviderOrderId: null,
            InvoiceReference: response.InvoiceId);
    }

    // ---------------- provider-order state (recovery & refund listing) ----------------

    public async Task<GatewayOrderSnapshot> GetOrderSnapshotAsync(string providerOrderId, CancellationToken ct = default)
    {
        return await RunReadAsync("get-order", ct, async token =>
        {
            try
            {
                var order = await _client.Orders.GetOrder(
                    id: providerOrderId,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);

                var units = order.PurchaseUnits ?? new List<PurchaseUnit>();

                var authorizations = units
                    .SelectMany(unit => unit.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
                    .Where(a => a.Id is not null)
                    .Select(a => new GatewayAuthorization(
                        AuthorizationId: a.Id!,
                        Status: a.Status?.Value ?? "UNKNOWN",
                        Amount: TryToDecimal(a.Amount?.Value) ?? 0m,
                        Currency: a.Amount?.CurrencyCode ?? string.Empty,
                        ExpirationTime: ParseTimestamp(a.ExpirationTime),
                        CreatedTime: null,
                        NetworkTransactionReference: a.NetworkTransactionReference?.Id,
                        ProviderOrderId: order.Id))
                    .ToList();

                var captures = units
                    .SelectMany(unit => unit.Payments?.Captures ?? new List<OrdersCapture>())
                    .Where(c => c.Id is not null)
                    .Select(c => new GatewayCapture(
                        CaptureId: c.Id!,
                        Status: c.Status?.Value ?? "UNKNOWN",
                        StatusReason: c.StatusDetails?.Reason?.Value,
                        GrossAmount: TryToDecimal(c.Amount?.Value) ?? 0m,
                        FeeAmount: c.SellerReceivableBreakdown?.PaypalFee is { } fee ? TryToDecimal(fee.Value) : null,
                        NetAmount: c.SellerReceivableBreakdown?.NetAmount is { } net ? TryToDecimal(net.Value) : null,
                        Currency: c.Amount?.CurrencyCode ?? string.Empty,
                        AuthorizationId: null,
                        ProviderOrderId: order.Id))
                    .ToList();

                var refunds = units
                    .SelectMany(unit => unit.Payments?.Refunds ?? new List<Refund>())
                    .Where(r => r.Id is not null)
                    .Select(r => new GatewayRefund(
                        RefundId: r.Id!,
                        Status: r.Status?.Value ?? "UNKNOWN",
                        Amount: TryToDecimal(r.Amount?.Value) ?? 0m,
                        Currency: r.Amount?.CurrencyCode ?? string.Empty,
                        TotalRefundedAmount: r.SellerPayableBreakdown?.TotalRefundedAmount is { } total ? TryToDecimal(total.Value) : null,
                        CaptureId: null,
                        ProviderOrderId: order.Id,
                        InvoiceReference: r.InvoiceId))
                    .ToList();

                return new GatewayOrderSnapshot(order.Id ?? providerOrderId, order.Status?.Value ?? "UNKNOWN", authorizations, captures, refunds);
            }
            catch (SdkException<GetOrderError> ex)
            {
                throw FromPaymentsError("get-order", ex.Error.TryGetError(out var e) ? e : null, null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        });
    }

    // ---------------- vault ----------------

    public async Task<SavedVaultCard> VaultCardAsync(string merchantCustomerId, CardCredential card, CancellationToken ct = default)
    {
        return await RunWriteAsync("vault-card", ct, async token =>
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = card.Number,
                        Expiry = WireExpiry(card.Expiry),
                        SecurityCode = card.SecurityCode,
                        Name = card.CardholderName
                    }
                }
            };

            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: null,
                    body: body,
                    ct: token);

                return MapPaymentToken(response)
                    ?? throw Protocol("vault-card", "the provider returned no payment token.");
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw FromVaultError("vault-card", ex);
            }
        });
    }

    public async Task<IReadOnlyList<SavedVaultCard>> ListVaultCardsAsync(string vaultCustomerId, CancellationToken ct = default)
    {
        return await RunReadAsync("list-vault-cards", ct, async token =>
        {
            try
            {
                var response = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: vaultCustomerId,
                    totalRequired: true,
                    ct: token);

                return (response.PaymentTokens ?? new List<PaymentTokenResponse>())
                    .Select(MapPaymentToken)
                    .Where(c => c is not null)
                    .Select(c => c!)
                    .ToList();
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                throw FromVaultError("list-vault-cards", ex);
            }
        });
    }

    public async Task DeleteVaultCardAsync(string tokenId, CancellationToken ct = default)
    {
        await RunWriteAsync<object?>("delete-vault-card", ct, async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: tokenId, ct: token);
                return null;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw FromVaultError("delete-vault-card", ex);
            }
        });
    }

    private static SavedVaultCard? MapPaymentToken(PaymentTokenResponse response)
    {
        if (response.Id is not { Length: > 0 } tokenId)
        {
            return null;
        }
        var card = response.PaymentSource?.Card;
        return new SavedVaultCard(
            TokenId: tokenId,
            VaultCustomerId: response.Customer?.Id,
            MerchantCustomerId: response.Customer?.MerchantCustomerId,
            Brand: card?.Brand?.Value,
            Last4: card?.LastDigits,
            Expiry: card?.Expiry,
            CardholderName: card?.Name);
    }

    // ---------------- transaction reporting ----------------

    /// <summary>PayPal's transaction-search endpoint supports at most 31-day windows.</summary>
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await RunReadAsync("search-transactions", ct, async token =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<GatewayTransaction>();

            for (var windowStart = from; windowStart < to;)
            {
                var windowEnd = windowStart + MaxSearchWindow < to ? windowStart + MaxSearchWindow : to;
                var collected = 0;

                for (var page = 1; ; page++)
                {
                    SearchResponse response;
                    try
                    {
                        response = await _client.TransactionSearch.SearchTransactions(
                            startDate: Rfc3339(windowStart),
                            endDate: Rfc3339(windowEnd),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: null,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "transaction_info",
                            balanceAffectingRecordsOnly: "N",
                            pageSize: 100,
                            page: page,
                            ct: token);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        throw FromRawError("search-transactions", ex.Error);
                    }

                    var items = response.TransactionDetails ?? new List<TransactionDetails>();
                    foreach (var item in items)
                    {
                        var mapped = MapTransaction(item);
                        if (mapped is not null && seen.Add(mapped.TransactionId))
                        {
                            results.Add(mapped);
                        }
                    }

                    collected += items.Count;

                    // With totals present they decide; only when the provider gives none
                    // does the short-page heuristic end the walk.
                    bool hasMore;
                    if (response.TotalPages is { } totalPages)
                    {
                        hasMore = page < totalPages;
                    }
                    else if (response.TotalItems is { } totalItems)
                    {
                        hasMore = collected < totalItems;
                    }
                    else
                    {
                        hasMore = items.Count >= 100;
                    }
                    if (!hasMore)
                    {
                        break;
                    }
                }

                windowStart = windowEnd;
            }

            return results;
        });
    }

    private static GatewayTransaction? MapTransaction(TransactionDetails details)
    {
        var info = details.TransactionInfo;
        if (info?.TransactionId is not { Length: > 0 } transactionId)
        {
            return null;
        }

        var amount = TryToDecimal(info.TransactionAmount?.Value);
        var fee = TryToDecimal(info.FeeAmount?.Value);
        decimal? net = amount.HasValue && fee.HasValue ? amount.Value - Math.Abs(fee.Value) : null;

        return new GatewayTransaction(
            TransactionId: transactionId,
            TransactionStatus: info.TransactionStatus,
            TransactionEventCode: info.TransactionEventCode,
            Amount: amount,
            FeeAmount: fee,
            NetAmount: net,
            Currency: info.TransactionAmount?.CurrencyCode,
            InvoiceId: info.InvoiceId,
            CustomField: info.CustomField,
            PaypalReferenceId: info.PaypalReferenceId,
            PaypalReferenceIdType: info.PaypalReferenceIdType?.Value,
            PaymentMethodType: info.PaymentMethodType,
            TransactionInitiationDate: ParseTimestamp(info.TransactionInitiationDate),
            TransactionUpdatedDate: ParseTimestamp(info.TransactionUpdatedDate));
    }

    // ---------------- execution wrappers ----------------

    /// <summary>
    /// A provider write: one logical call, at most one authorised send of each request it
    /// makes, whole-call budget. If a send left this process and the connection then failed,
    /// the outcome is reported as unknown — never as a clean failure the caller may blindly retry.
    /// </summary>
    private async Task<T> RunWriteAsync<T>(string operation, CancellationToken ct, Func<CancellationToken, Task<T>> write)
    {
        using var scope = SingleSendScope.Begin();
        try
        {
            return await RunGuarded(operation, ct, write);
        }
        catch (PaymentResendBlockedException ex)
        {
            _logger.LogWarning($"PayPal {operation}: a retry was blocked after a failed send — outcome unknown.");
            throw new PaymentGatewayException(
                PaymentFailureKind.OutcomeUnknown,
                $"The payment provider connection failed while a {Describe(operation)} was in flight, so its outcome is unknown. " +
                "Re-read the order's payment state (pay/fulfil/refund replays recover it) before retrying.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            if (scope.OutcomeIsUnknown)
            {
                throw new PaymentGatewayException(
                    PaymentFailureKind.OutcomeUnknown,
                    $"The payment provider connection dropped while a {Describe(operation)} was sent; its outcome is unknown. " +
                    "Re-read the order's payment state before retrying.", ex);
            }
            throw new PaymentGatewayException(
                PaymentFailureKind.Unreachable,
                "The payment provider could not be reached; nothing was changed.", ex);
        }
    }

    private async Task<T> RunReadAsync<T>(string operation, CancellationToken ct, Func<CancellationToken, Task<T>> read)
    {
        try
        {
            return await RunGuarded(operation, ct, read);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException(
                PaymentFailureKind.Unreachable,
                "The payment provider could not be reached.", ex);
        }
    }

    private async Task<T> RunGuarded<T>(string operation, CancellationToken ct, Func<CancellationToken, Task<T>> call)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Either a 2xx body that no longer matches the model (outcome unknown) or a
            // non-2xx body that the generated error model cannot parse (a rejection whose
            // detail was lost). The caller must never see this as a plain 5xx it can retry
            // blindly — mark unknown so replays settle from provider state.
            _logger.LogWarning($"PayPal {operation}: response body could not be processed ({ex.GetType().Name}).");
            throw new PaymentGatewayException(
                PaymentFailureKind.OutcomeUnknown,
                $"The payment provider returned a response that could not be processed for {Describe(operation)}. " +
                "Re-read the order's payment state to settle the outcome.", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TaskCanceledException && budget.IsCancellationRequested)
        {
            // The call budget expired. If a send had already been attempted, the provider
            // may hold an outcome we never read — report that honestly.
            var kind = SingleSendScope.Current?.AnySendAttempted == true
                ? PaymentFailureKind.OutcomeUnknown
                : PaymentFailureKind.Unreachable;
            throw new PaymentGatewayException(
                kind,
                kind == PaymentFailureKind.OutcomeUnknown
                    ? $"The payment provider did not answer the {Describe(operation)} within the allowed time after it was sent. Re-read the order's payment state to settle the outcome."
                    : $"The payment provider did not answer the {Describe(operation)} within the allowed time.", ex);
        }
    }

    // ---------------- error translation ----------------

    private static PaymentGatewayException FromTypedError(string operation, ErrorModel? error)
    {
        if (error is null)
        {
            return Protocol(operation, "the provider error body could not be read.");
        }

        var detail = error.Details?.FirstOrDefault();
        var issue = detail is null ? null : detail.Issue;
        // detail.Value is deliberately NOT included: the provider can echo submitted field
        // values (e.g. a card number) in validation errors — they must never reach a log or a response.
        var description = detail?.Description;
        var kind = ClassifyName(error.Name);

        var message = $"The payment provider rejected the {Describe(operation)}" +
                      (string.IsNullOrEmpty(error.Message) ? "." : $": {error.Message}") +
                      (description is null ? string.Empty : $" ({description})");

        return new PaymentGatewayException(kind, message, providerErrorName: error.Name, providerIssue: issue ?? description);
    }

    private static PaymentGatewayException FromRawError(string operation, RawError? raw)
    {
        if (raw is null)
        {
            return Protocol(operation, "the provider returned no error detail.");
        }

        var kind = raw.StatusCode switch
        {
            HttpStatusCode.NotFound => PaymentFailureKind.ResourceNotFound,
            HttpStatusCode.Conflict => PaymentFailureKind.Conflict,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PaymentFailureKind.AuthenticationFailed,
            _ => PaymentFailureKind.ProviderRejected
        };

        var body = SafeBody(raw);
        var message = $"The payment provider rejected the {Describe(operation)} with HTTP {(int)raw.StatusCode}." +
                      (body is null ? string.Empty : $" {body}");

        return new PaymentGatewayException(kind, message, providerErrorName: raw.StatusCode.ToString());
    }

    private static PaymentGatewayException FromPaymentsError(string operation, ErrorModel? typed, RawError? noContent, RawError? raw)
    {
        if (typed is not null)
        {
            return FromTypedError(operation, typed);
        }
        if (noContent is not null)
        {
            return FromRawError(operation, noContent);
        }
        if (raw is not null)
        {
            return FromRawError(operation, raw);
        }
        return Protocol(operation, "the provider error body could not be read.");
    }

    private static PaymentGatewayException FromVaultError(string operation, Exception ex) =>
        ex switch
        {
            SdkException<CreatePaymentTokenError> e when e.Error.TryGetError1(out var error) => FromTypedModel1(operation, error),
            SdkException<CreatePaymentTokenError> e when e.Error.TryGetRawError(out var raw) => FromRawError(operation, raw),
            SdkException<ListCustomerPaymentTokensError> e when e.Error.TryGetError1(out var error) => FromTypedModel1(operation, error),
            SdkException<ListCustomerPaymentTokensError> e when e.Error.TryGetRawError(out var raw) => FromRawError(operation, raw),
            SdkException<DeletePaymentTokenError> e when e.Error.TryGetError1(out var error) => FromTypedModel1(operation, error),
            SdkException<DeletePaymentTokenError> e when e.Error.TryGetRawError(out var raw) => FromRawError(operation, raw),
            SdkException<GetPaymentTokenError> e when e.Error.TryGetError1(out var error) => FromTypedModel1(operation, error),
            SdkException<GetPaymentTokenError> e when e.Error.TryGetRawError(out var raw) => FromRawError(operation, raw),
            _ => Protocol(operation, "the provider error body could not be read.")
        };

    private static PaymentGatewayException FromTypedModel1(string operation, Error1 error)
    {
        var detail = error.Details?.FirstOrDefault();
        // detail.Value is deliberately NOT included: validation errors can echo submitted
        // field values (e.g. a card number) — they must never reach a log or a response.
        var kind = ClassifyName(error.Name);
        var description = detail?.Description;

        var message = $"The payment provider rejected the {Describe(operation)}" +
                      (string.IsNullOrEmpty(error.Message) ? "." : $": {error.Message}") +
                      (description is null ? string.Empty : $" ({description})");

        return new PaymentGatewayException(kind, message, providerErrorName: error.Name, providerIssue: detail?.Issue ?? description);
    }

    private static PaymentFailureKind ClassifyName(string name)
    {
        if (name.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return PaymentFailureKind.ResourceNotFound;
        if (name.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) || name.Contains("ALREADY", StringComparison.OrdinalIgnoreCase)) return PaymentFailureKind.Conflict;
        if (name.Contains("AUTH", StringComparison.OrdinalIgnoreCase) || name.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)) return PaymentFailureKind.AuthenticationFailed;
        return PaymentFailureKind.ProviderRejected;
    }

    private static string? SafeBody(RawError raw)
    {
        try
        {
            var text = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            text = text.Length > 300 ? text[..300] + "…" : text;
            return text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PaymentGatewayException Protocol(string operation, string reason) =>
        new(PaymentFailureKind.ProtocolError,
            $"The payment provider responded to {Describe(operation)} in an unexpected way. Re-read the order's payment state before retrying.",
            providerIssue: reason);

    // ---------------- small helpers ----------------

    private static string MoneyValue(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// PayPal's card expiry wire format is ISO-8601 YYYY-MM (Models/CardRequest.cs regex
    /// ^[0-9]{4}-(0[1-9]|1[0-2])$; confirmed live: MM/YYYY is rejected with INVALID_REQUEST).
    /// The app's canonical display form is MM/YYYY.
    /// </summary>
    private static string WireExpiry(string expiry)
    {
        var normalized = expiry.Replace('-', '/');
        var parts = normalized.Split('/');
        if (parts.Length == 2 && parts[1].Length == 4)
        {
            return $"{parts[1]}-{parts[0]}";
        }
        return expiry;
    }

    private static decimal ToDecimal(Money? money, string operation) =>
        TryToDecimal(money?.Value) ?? throw Protocol(operation, "the provider returned an unreadable amount.");

    private static decimal? TryToDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Describe(string operation) => operation switch
    {
        "authorize" => "payment authorization",
        "authorize-existing-order" => "payment authorization",
        "capture" => "payment capture",
        "reauthorize" => "re-authorization",
        "void" => "authorization release",
        "refund" => "refund",
        "vault-card" => "card save",
        "delete-vault-card" => "card removal",
        "search-transactions" => "transaction report",
        _ => operation
    };
}

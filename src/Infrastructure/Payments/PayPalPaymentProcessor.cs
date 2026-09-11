using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentProcessor"/>. All PayPal SDK types are confined to
/// this class; it maps between the application's provider-agnostic contracts and the SDK, translates
/// every SDK/transport failure into <see cref="PaymentProcessingException"/>, and bounds each whole
/// call with a CancellationToken deadline.
/// </summary>
public class PayPalPaymentProcessor : IPaymentProcessor
{
    private const int MaxReconciliationPages = 200;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentProcessor> _logger;

    public PayPalPaymentProcessor(PayPalServerSdkClient client, PayPalSettings settings,
        ILogger<PayPalPaymentProcessor> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        var order = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    CustomId = request.CustomId,
                    InvoiceId = request.InvoiceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = FormatAmount(request.Amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(request) }
        };

        return await Bounded(async token =>
        {
            try
            {
                // Creating an order with a card payment source and intent=AUTHORIZE places the hold
                // as part of the create (return=representation surfaces the authorization). Only when
                // the create did not yield an authorization do we call AuthorizeOrder explicitly.
                var created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"create-{request.IdempotencyKey}",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: order,
                    prefer: "return=representation",
                    ct: token);

                var payPalOrderId = created.Id
                    ?? throw new PaymentProcessingException("PayPal did not return an order id on create.");

                if (IsPayerActionRequired(created.Status?.Value))
                    throw new PaymentApprovalRequiredException(
                        "This card requires the shopper to approve the payment in a browser (3-D Secure). " +
                        "This integration does not perform an approval round-trip.");

                var auth = ExtractAuthorization(created.PurchaseUnits);
                if (auth?.Id is null)
                {
                    var authorized = await _client.Orders.AuthorizeOrder(
                        id: payPalOrderId,
                        payPalMockResponse: null,
                        payPalRequestId: $"authorize-{request.IdempotencyKey}",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        ct: token);

                    if (IsPayerActionRequired(authorized.Status?.Value))
                        throw new PaymentApprovalRequiredException(
                            "This card requires the shopper to approve the payment in a browser (3-D Secure). " +
                            "This integration does not perform an approval round-trip.");

                    auth = authorized.PurchaseUnits?
                        .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
                }

                if (auth?.Id is null)
                    throw new PaymentProcessingException(
                        $"PayPal did not return an authorization for order {payPalOrderId} " +
                        $"(order status {created.Status?.Value ?? "unknown"}).");

                return new AuthorizationResult
                {
                    PayPalOrderId = payPalOrderId,
                    AuthorizationId = auth.Id,
                    Status = auth.Status?.Value,
                    ExpiresAt = ParseDate(auth.ExpirationTime)
                };
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw Translate("create order", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw Translate("authorize order", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string payPalOrderId, string authorizationId,
        string currencyCode, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
        };

        return await Bounded(async token =>
        {
            try
            {
                var reauth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token);

                if (reauth.Id is null)
                    throw new PaymentProcessingException("PayPal returned no authorization id on reauthorize.");

                return new AuthorizationResult
                {
                    PayPalOrderId = payPalOrderId,
                    AuthorizationId = reauth.Id,
                    Status = reauth.Status?.Value,
                    ExpiresAt = ParseDate(reauth.ExpirationTime)
                };
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw Translate("reauthorize payment", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string currencyCode, decimal amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) },
            FinalCapture = true
        };

        return await Bounded(async token =>
        {
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token);

                if (captured.Id is null)
                    throw new PaymentProcessingException("PayPal returned no capture id.");

                var breakdown = captured.SellerReceivableBreakdown;
                var gross = ParseMoney(breakdown?.GrossAmount?.Value) ?? ParseMoney(captured.Amount?.Value) ?? amount;

                return new CaptureResult
                {
                    CaptureId = captured.Id,
                    Status = captured.Status?.Value,
                    CapturedAmount = gross,
                    PayPalFee = ParseMoney(breakdown?.PaypalFee?.Value),
                    NetAmount = ParseMoney(breakdown?.NetAmount?.Value)
                };
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw Translate("capture payment", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        await Bounded<bool>(async token =>
        {
            try
            {
                // return=representation so PayPal answers 200 + body; under the default return=minimal
                // it answers 204 No Content, which the SDK cannot deserialize.
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    ct: token);
                return true;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw Translate("void payment", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task<RefundResult> RefundAsync(string captureId, string currencyCode, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        // A null amount means a full refund (omit the amount from the request body entirely).
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } }
            : null;

        return await Bounded(async token =>
        {
            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token);

                if (refund.Id is null)
                    throw new PaymentProcessingException("PayPal returned no refund id.");

                return new RefundResult
                {
                    RefundId = refund.Id,
                    Status = refund.Status?.Value,
                    Amount = ParseMoney(refund.Amount?.Value) ?? amount ?? 0m
                };
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw Translate("refund payment", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct = default)
    {
        var card = request.Card;
        var body = new PaymentTokenRequest
        {
            Customer = string.IsNullOrEmpty(request.PayPalCustomerId)
                ? null
                : new Customer { Id = request.PayPalCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: request.IdempotencyKey,
                    body: body,
                    ct: token);

                if (response.Id is null)
                    throw new PaymentProcessingException("PayPal returned no vault token id.");

                var savedCard = response.PaymentSource?.Card;
                return new VaultCardResult
                {
                    VaultTokenId = response.Id,
                    PayPalCustomerId = response.Customer?.Id,
                    Brand = savedCard?.Brand?.Value ?? "CARD",
                    LastDigits = savedCard?.LastDigits ?? "----",
                    Expiry = savedCard?.Expiry,
                    CardholderName = savedCard?.Name
                };
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw Translate("save card", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await Bounded<bool>(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: token);
                return true;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw Translate("delete saved card", ex, ex.Error.TryGetError(out var e) ? e : null,
                    ex.Error.TryGetRawError(out var r) ? r : null, write: true);
            }
        }, ct, write: true);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var start = FormatSearchDate(from);
        var end = FormatSearchDate(to);
        var results = new List<PayPalTransaction>();

        return await Bounded(async token =>
        {
            int page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
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
                        page: page,
                        ct: token);
                }
                catch (SdkException<RawError> ex)
                {
                    // Case B: RawError carries the status directly.
                    _logger.LogWarning("PayPal transaction search failed at HTTP {Status}", (int)ex.Error.StatusCode);
                    throw new PaymentProcessingException(
                        $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).",
                        (int)ex.Error.StatusCode, inner: ex);
                }

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new PayPalTransaction
                    {
                        TransactionId = info.TransactionId,
                        Status = info.TransactionStatus,
                        Amount = ParseMoney(info.TransactionAmount?.Value),
                        CurrencyCode = info.TransactionAmount?.CurrencyCode,
                        Fee = ParseMoney(info.FeeAmount?.Value),
                        InvoiceId = info.InvoiceId,
                        CustomId = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        InitiationDate = ParseDate(info.TransactionInitiationDate)
                    });
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || page >= MaxReconciliationPages) break;
                page++;
            }

            if (page >= MaxReconciliationPages)
                _logger.LogWarning("Reconciliation stopped at the {Max}-page cap; results may be truncated.",
                    MaxReconciliationPages);

            return (IReadOnlyList<PayPalTransaction>)results;
        }, ct, write: false);
    }

    // --- helpers ---

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private CardRequest BuildCardRequest(AuthorizeRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultTokenId))
            return new CardRequest { VaultId = request.VaultTokenId };

        var card = request.Card!;
        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrEmpty(card.BillingCountryCode))
            return null;
        return new Address
        {
            CountryCode = card.BillingCountryCode!,
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea1 = card.BillingAdminArea1,
            AdminArea2 = card.BillingAdminArea2,
            PostalCode = card.BillingPostalCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d : null;

    private static bool IsPayerActionRequired(string? status) =>
        string.Equals(status, OrderStatus.PayerActionRequired.Value, StringComparison.OrdinalIgnoreCase);

    private PaymentProcessingException Translate(string operation, Exception ex, Error? typed, RawError? raw,
        bool write)
    {
        int? status = raw is not null ? (int)raw.StatusCode : null;
        string? debugId = typed?.DebugId;

        string message;
        if (typed is not null)
        {
            var issue = typed.Details?.FirstOrDefault()?.Issue;
            message = $"PayPal {operation} failed: {typed.Name}" +
                      (issue is not null ? $" / {issue}" : string.Empty) + $" — {typed.Message}";
        }
        else if (raw is not null)
        {
            message = $"PayPal {operation} failed (HTTP {(int)raw.StatusCode}).";
        }
        else
        {
            message = $"PayPal {operation} failed.";
        }

        _logger.LogWarning("PayPal {Operation} error: status={Status} debugId={DebugId} name={Name}",
            operation, status, debugId ?? "-", typed?.Name ?? "-");

        return new PaymentProcessingException(message, status, debugId, ex);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, bool write)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TotalTimeoutSeconds));
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentProcessingException)
        {
            throw; // already translated
        }
        catch (System.Text.Json.JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that did not match its
            // generated error shape — either way the outcome detail is unreadable.
            throw new PaymentProcessingException(
                "PayPal returned a response that could not be processed.", null, null, ex)
            { OutcomeUnknown = write };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled — propagate
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Transport failure or our own timeout: for a write the outcome is unknown (bytes may have
            // reached PayPal) and must be reconciled, not assumed failed.
            throw new PaymentProcessingException("PayPal is currently unreachable.", null, null, ex)
            { OutcomeUnknown = write };
        }
    }
}

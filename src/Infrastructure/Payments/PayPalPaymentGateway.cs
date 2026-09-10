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
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPal Server SDK. Translates SDK
/// success/error shapes into the application's SDK-free DTOs and <see cref="PaymentGatewayException"/>s.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    // PayPal's transaction-search window is limited to 31 days per request; we chunk wider ranges.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int MaxSearchPagesPerWindow = 1000; // safety bound so a page loop can never run away.

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly string _currency;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(60);

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
        _currency = options.Value.Currency;
    }

    public string Currency => _currency;

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, string invoiceReference,
        CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct)
    {
        if (card is null && string.IsNullOrWhiteSpace(vaultId))
            throw new PaymentGatewayException("Either card details or a saved-card id must be supplied to authorize a payment.");

        using var scope = Budget(ct);
        var budgetCt = scope.Token;

        var paymentSource = new PaymentSource
        {
            Card = card is not null
                ? new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card),
                }
                : new CardRequest { VaultId = vaultId }
        };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceReference,
                    CustomId = invoiceReference,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = _currency,
                        Value = FormatAmount(amount),
                    }
                }
            },
            PaymentSource = paymentSource,
        };

        var created = await Call<Order, CreateOrderError>("CreateOrder",
            c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{idempotencyKey}-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: c),
            e => e.TryGetError(out var v) ? v : null, null, budgetCt);

        var payPalOrderId = created.Id
            ?? throw new PaymentGatewayException("CreateOrder did not return an order id.");

        // A direct (unbranded) card payment should not require buyer approval. If PayPal asks for one,
        // stop and report rather than building a browser approval round-trip.
        if (RequiresBuyerApproval(created.Status, created.Links))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (payer action / 3-D Secure). " +
                "This integration does not perform browser approval; the payment cannot be completed unattended.");
        }

        // For a direct card with intent=AUTHORIZE, the authorization is created at order-creation time and is
        // already present on the response — use it. Only if it is absent (e.g. a wallet flow) do we place the
        // authorization explicitly via AuthorizeOrder.
        var authorization = created.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization?.Id is null)
        {
            var authResp = await Call<OrderAuthorizeResponse, AuthorizeOrderError>("AuthorizeOrder",
                c => _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: c),
                e => e.TryGetError(out var v) ? v : null, null, budgetCt);

            if (RequiresBuyerApproval(authResp.Status, authResp.Links))
            {
                throw new PaymentChallengeRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser. This integration does not perform browser approval.");
            }

            authorization = authResp.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal accepted the order but returned no authorization to hold the funds.");
        }

        return new PaymentAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id,
            Status = EnumValue(authorization.Status),
            ExpiresAt = authorization.ExpirationTime,
            Amount = ParseMoney(authorization.Amount) ?? amount,
        };
    }

    public async Task<PaymentAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct)
    {
        using var scope = Budget(ct);

        var reauth = await Call<PaymentAuthorization, ReauthorizePaymentError>("ReauthorizePayment",
            c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"{idempotencyKey}-reauth",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = _currency, Value = FormatAmount(amount) } },
                prefer: "return=representation",
                ct: c),
            e => e.TryGetError(out var v) ? v : null,
            e => e.TryGetNoContent(out var v) ? v : null, scope.Token);

        if (reauth.Id is null)
            throw new PaymentGatewayException("Reauthorization did not return a new authorization id.");

        return new PaymentAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = reauth.Id,
            Status = EnumValue(reauth.Status),
            ExpiresAt = reauth.ExpirationTime,
            Amount = ParseMoney(reauth.Amount) ?? amount,
        };
    }

    public async Task<PaymentAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var scope = Budget(ct);

        var auth = await Call<PaymentAuthorization, GetAuthorizedPaymentError>("GetAuthorizedPayment",
            c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: c),
            e => e.TryGetError(out var v) ? v : null,
            e => e.TryGetNoContent(out var v) ? v : null, scope.Token);

        return new PaymentAuthorizationInfo
        {
            Status = EnumValue(auth.Status),
            ExpiresAt = auth.ExpirationTime,
        };
    }

    public async Task<PaymentCaptureResult> CaptureAsync(string authorizationId, string invoiceReference,
        string idempotencyKey, CancellationToken ct)
    {
        using var scope = Budget(ct);

        var capture = await Call<CapturedPayment, CaptureAuthorizedPaymentError>("CaptureAuthorizedPayment",
            c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true, InvoiceId = invoiceReference },
                prefer: "return=representation",
                ct: c),
            e => e.TryGetError(out var v) ? v : null,
            e => e.TryGetNoContent(out var v) ? v : null, scope.Token);

        if (capture.Id is null)
            throw new PaymentGatewayException("Capture did not return a capture id.");

        var breakdown = capture.SellerReceivableBreakdown;
        return new PaymentCaptureResult
        {
            CaptureId = capture.Id,
            Status = EnumValue(capture.Status),
            GrossAmount = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? 0m,
            PayPalFee = ParseMoney(breakdown?.PaypalFee),
            NetAmount = ParseMoney(breakdown?.NetAmount),
        };
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var scope = Budget(ct);

        await Call<PaymentAuthorization, VoidPaymentError>("VoidPayment",
            c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"{idempotencyKey}-void",
                ct: c),
            e => e.TryGetError(out var v) ? v : null,
            e => e.TryGetNoContent(out var v) ? v : null, scope.Token);
    }

    public async Task<PaymentRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        using var scope = Budget(ct);

        // Full refund => empty body; partial refund => an amount object.
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = FormatAmount(amount.Value) } };

        var refund = await Call<Refund, RefundCapturedPaymentError>("RefundCapturedPayment",
            c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c),
            e => e.TryGetError(out var v) ? v : null,
            e => e.TryGetNoContent(out var v) ? v : null, scope.Token);

        if (refund.Id is null)
            throw new PaymentGatewayException("Refund did not return a refund id.");

        return new PaymentRefundResult
        {
            RefundId = refund.Id,
            Status = EnumValue(refund.Status),
            Amount = ParseMoney(refund.Amount) ?? amount ?? 0m,
        };
    }

    public async Task<SavedCardResult> VaultCardAsync(string customerReference, CardDetails card, CancellationToken ct)
    {
        using var scope = Budget(ct);
        var budgetCt = scope.Token;

        var setupRequest = new SetupTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(customerReference)
                ? null
                : new Customer { MerchantCustomerId = customerReference },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card),
                }
            }
        };

        var setup = await Call<SetupTokenResponse, CreateSetupTokenError>("CreateSetupToken",
            c => _client.Vault.CreateSetupToken(payPalRequestId: null, body: setupRequest, ct: c),
            e => e.TryGetError(out var v) ? v : null, null, budgetCt);

        if (setup.Id is null)
            throw new PaymentGatewayException("CreateSetupToken did not return a setup token id.");

        var tokenRequest = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setup.Id,
                    Type = VaultTokenRequestType.SetupToken,
                }
            }
        };

        var token = await Call<PaymentTokenResponse, CreatePaymentTokenError>("CreatePaymentToken",
            c => _client.Vault.CreatePaymentToken(payPalRequestId: null, body: tokenRequest, ct: c),
            e => e.TryGetError(out var v) ? v : null, null, budgetCt);

        if (token.Id is null)
            throw new PaymentGatewayException("CreatePaymentToken did not return a payment token id.");

        var vaultedCard = token.PaymentSource?.Card;
        return new SavedCardResult
        {
            VaultId = token.Id,
            CardBrand = EnumValue(vaultedCard?.Brand),
            LastFourDigits = vaultedCard?.LastDigits,
            Expiry = vaultedCard?.Expiry,
            CardholderName = vaultedCard?.Name ?? card.CardholderName,
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var scope = Budget(ct);

        await Call<bool, DeletePaymentTokenError>("DeletePaymentToken",
            async c =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: c);
                return true;
            },
            e => e.TryGetError(out var v) ? v : null, null, scope.Token);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        using var scope = Budget(ct);
        var budgetCt = scope.Token;

        var results = new List<PayPalTransaction>();

        // Walk the whole range in <=31-day windows; page each window to completion.
        for (var windowStart = from; windowStart < to; windowStart = windowStart.Add(MaxSearchWindow))
        {
            var windowEnd = windowStart.Add(MaxSearchWindow);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var pagesFetched = 0;
            int totalPages;
            do
            {
                var pageNumber = page;
                var response = await Call<SearchResponse, RawError>("SearchTransactions",
                    c => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatDate(windowStart),
                        endDate: FormatDate(windowEnd),
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
                        page: pageNumber,
                        ct: c),
                    typedError: null, noContent: null, budgetCt);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        results.Add(new PayPalTransaction
                        {
                            TransactionId = info.TransactionId,
                            PayPalReferenceId = info.PaypalReferenceId,
                            InvoiceId = info.InvoiceId,
                            Amount = ParseMoney(info.TransactionAmount),
                            CurrencyCode = info.TransactionAmount?.CurrencyCode,
                            FeeAmount = ParseMoney(info.FeeAmount),
                            Status = info.TransactionStatus,
                            InitiationDate = ParseDate(info.TransactionInitiationDate),
                        });
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
                pagesFetched++;
            }
            while (page <= totalPages && pagesFetched < MaxSearchPagesPerWindow);
        }

        return results;
    }

    // ---- Helpers -------------------------------------------------------------------------------------

    private CancellationTokenScope Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);
        return new CancellationTokenScope(cts);
    }

    private readonly struct CancellationTokenScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        public CancellationTokenScope(CancellationTokenSource cts) => _cts = cts;
        public CancellationToken Token => _cts.Token;
        public void Dispose() => _cts.Dispose();
    }

    /// <summary>
    /// Executes an SDK call and translates its two error families into <see cref="PaymentGatewayException"/>:
    /// a typed <c>{Operation}Error</c> (Case A), or <see cref="RawError"/> directly (Case B, e.g. search).
    /// Also converts a drifted/unparseable body (JsonException) and transport failures.
    /// </summary>
    private async Task<T> Call<T, TErr>(string op, Func<CancellationToken, Task<T>> action,
        Func<TErr, Error?>? typedError, Func<TErr, RawError?>? noContent, CancellationToken ct)
        where TErr : class
    {
        PayPalResponseContext.Begin();
        try
        {
            return await action(ct);
        }
        catch (SdkException<TErr> ex)
        {
            throw Translate(op, ex.Error, typedError, noContent);
        }
        catch (JsonException ex)
        {
            // A 204 No Content has no body to deserialize; that is an empty success, not drift. Operations
            // whose result we ignore (e.g. void) return default here.
            if (PayPalResponseContext.LastStatusCode == 204)
            {
                return default!;
            }

            _logger.LogError(ex, "PayPal {Operation}: response could not be deserialized.", op);
            throw new PaymentGatewayException(
                $"The payment provider returned a response that could not be processed ({op}).", inner: ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation / total-budget deadline — surface as-is.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "PayPal {Operation}: provider unreachable.", op);
            throw new PaymentGatewayException($"The payment provider is unreachable ({op}).", inner: ex);
        }
    }

    private PaymentGatewayException Translate<TErr>(string op, TErr error, Func<TErr, Error?>? typedError,
        Func<TErr, RawError?>? noContent) where TErr : class
    {
        var status = PayPalResponseContext.LastStatusCode;

        // Case B: the error model IS a RawError.
        if (error is RawError rawDirect)
        {
            var raw = Truncate(rawDirect.ReadAsString());
            _logger.LogWarning("PayPal {Operation} failed: HTTP {Status} {Body}", op, (int)rawDirect.StatusCode, raw);
            return new PaymentGatewayException($"{op} failed (HTTP {(int)rawDirect.StatusCode}).",
                (int)rawDirect.StatusCode, providerErrorName: raw);
        }

        // Case A: typed Error body carries the provider's name/message/debug_id (but no status).
        var typed = typedError?.Invoke(error);
        if (typed is not null)
        {
            _logger.LogWarning("PayPal {Operation} failed: {Name} '{Message}' (debug_id={DebugId}, http={Status})",
                op, typed.Name, typed.Message, typed.DebugId, status);
            return new PaymentGatewayException($"{op} failed: {typed.Message}", status, typed.DebugId, typed.Name);
        }

        var nc = noContent?.Invoke(error);
        if (nc is not null)
        {
            _logger.LogWarning("PayPal {Operation} failed: HTTP {Status} (no content).", op, (int)nc.StatusCode);
            return new PaymentGatewayException($"{op} failed (HTTP {(int)nc.StatusCode}).", (int)nc.StatusCode);
        }

        if (error is ApiError apiError && apiError.TryGetRawError(out var raw2))
        {
            _logger.LogWarning("PayPal {Operation} failed: HTTP {Status} {Body}", op, (int)raw2.StatusCode,
                Truncate(raw2.ReadAsString()));
            return new PaymentGatewayException($"{op} failed (HTTP {(int)raw2.StatusCode}).", (int)raw2.StatusCode);
        }

        _logger.LogWarning("PayPal {Operation} failed with an unrecognised error shape (http={Status}).", op, status);
        return new PaymentGatewayException($"{op} failed.", status);
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.BillingAddressLine1)
            && string.IsNullOrWhiteSpace(card.BillingCountryCode)
            && string.IsNullOrWhiteSpace(card.BillingPostalCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode ?? "US",
        };
    }

    private static bool RequiresBuyerApproval(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status is not null && status == OrderStatus.PayerActionRequired)
            return true;

        return links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d : null;
    }

    private static string? EnumValue<T>(T? enumValue) where T : PayPalServerSdk.Core.Enum.StringEnum<T>
        => enumValue?.Value;

    private static string Truncate(string? s, int max = 500)
        => string.IsNullOrEmpty(s) ? string.Empty : (s!.Length <= max ? s : s.Substring(0, max));
}

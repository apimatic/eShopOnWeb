using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal-backed <see cref="IPaymentProcessor"/>. All SDK contract facts (signatures, wire names,
/// enum values, error handling) come from the grounded contract sheet in paypal-plan.md.
///
/// Flow: <c>CreateOrder(intent=AUTHORIZE, payment_source)</c> → <c>AuthorizeOrder</c> for the hold;
/// <c>CaptureAuthorizedPayment</c> at fulfilment; <c>ReauthorizePayment</c> for a stale hold;
/// <c>VoidPayment</c> to cancel; <c>RefundCapturedPayment</c> to refund; <c>Vault.*</c> for saved
/// cards; <c>TransactionSearch.SearchTransactions</c> for reconciliation.
/// </summary>
public class PayPalPaymentService : IPaymentProcessor
{
    // PayPal transaction-search caps each query window at ~31 days; chunk longer ranges.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentService> _logger;

    public PayPalPaymentService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency!;

    // Per-process prefix for PayPal-Request-Id idempotency keys. The keys handed in by callers are
    // deterministic per order (so a double-click within a run is deduped by PayPal), but order ids
    // reset with the in-memory store between runs; the prefix keeps a re-run from colliding with a
    // key PayPal already recorded, without weakening in-run idempotency.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N").Substring(0, 12);

    private static string RequestId(string key) => $"{InstanceId}-{key}";

    public Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        if (request.Card is null && string.IsNullOrWhiteSpace(request.VaultId))
        {
            throw new PaymentProcessorException("A card or a saved payment method is required to pay.", 400);
        }

        return Invoke("authorize", async () =>
        {
            var card = request.VaultId is { Length: > 0 } vaultId
                ? new CardRequest { VaultId = vaultId }
                : BuildCardRequest(request.Card!);

            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = Currency,
                            Value = FormatAmount(request.Amount)
                        },
                        // custom_id ties the transaction back to the local order for reconciliation.
                        // invoice_id is intentionally omitted: some accounts enforce global invoice-id
                        // uniqueness, and idempotency is already guaranteed by the PayPal-Request-Id key.
                        CustomId = request.OrderReference
                    }
                },
                PaymentSource = new PaymentSource { Card = card }
            };

            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: RequestId(idempotencyKey + "-order"),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);

            EnsureNoChallenge(created.Status);

            // With a card payment_source supplied at create time, PayPal authorizes the order during
            // CreateOrder itself — the hold is already present in the create response. Only call
            // AuthorizeOrder separately if the create did not already produce an authorization.
            var authorization = ExtractAuthorization(created.PurchaseUnits);

            if (authorization is null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: RequestId(idempotencyKey),
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);

                EnsureNoChallenge(authorized.Status);
                authorization = ExtractAuthorization(authorized.PurchaseUnits);
            }

            if (authorization?.Id is null)
            {
                throw new PaymentProcessorException("PayPal did not return an authorization for the order.", 502);
            }

            return new AuthorizationResult(
                PayPalOrderId: created.Id!,
                AuthorizationId: authorization.Id,
                Status: authorization.Status?.Value,
                ExpiresAt: ParseTimestamp(authorization.ExpirationTime));
        }, ct);
    }

    public Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        return Invoke("reauthorize", async () =>
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: RequestId(idempotencyKey),
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: ct);

            return new AuthorizationSnapshot(
                AuthorizationId: reauth.Id ?? authorizationId,
                Status: reauth.Status?.Value,
                ExpiresAt: ParseTimestamp(reauth.ExpirationTime));
        }, ct);
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct = default)
    {
        return Invoke("capture", async () =>
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: RequestId(idempotencyKey),
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) },
                    FinalCapture = true
                },
                prefer: "return=representation",
                ct: ct);

            if (captured.Id is null)
            {
                throw new PaymentProcessorException("PayPal did not return a capture for the payment.", 502);
            }

            var breakdown = captured.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(captured.Amount) ?? amount;

            return new CaptureResult(
                CaptureId: captured.Id,
                Status: captured.Status?.Value,
                GrossAmount: gross,
                PayPalFee: ParseMoney(breakdown?.PaypalFee),
                NetAmount: ParseMoney(breakdown?.NetAmount),
                CurrencyCode: Currency);
        }, ct);
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        return Invoke("void", async () =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: RequestId(idempotencyKey),
                    prefer: "return=minimal",
                    ct: ct);
            }
            catch (Exception) when (PayPalResponseContext.Current?.StatusCode is >= 200 and < 300)
            {
                // A successful void returns 204 No Content; the SDK throws trying to deserialize the
                // empty body. A 2xx status means the hold was released — treat it as success.
            }
        }, ct);
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        return Invoke("refund", async () =>
        {
            RefundRequest? body = amount is null
                ? null
                : new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount.Value) } };

            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: RequestId(idempotencyKey),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            if (refund.Id is null)
            {
                throw new PaymentProcessorException("PayPal did not return a refund id.", 502);
            }

            return new RefundResult(
                RefundId: refund.Id,
                Status: refund.Status?.Value,
                Amount: ParseMoney(refund.Amount) ?? amount ?? 0m,
                CurrencyCode: Currency);
        }, ct);
    }

    public Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        return Invoke("get-authorization", async () =>
        {
            var pa = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            return new AuthorizationSnapshot(
                AuthorizationId: pa.Id ?? authorizationId,
                Status: pa.Status?.Value,
                ExpiresAt: ParseTimestamp(pa.ExpirationTime));
        }, ct);
    }

    public Task<VaultedCard> VaultCardAsync(CardDetails card, string customerReference, string idempotencyKey, CancellationToken ct = default)
    {
        return Invoke("vault-card", async () =>
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = customerReference },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = BuildAddress(card.BillingAddress)
                    }
                }
            };

            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: RequestId(idempotencyKey),
                body: body,
                ct: ct);

            if (token.Id is null)
            {
                throw new PaymentProcessorException("PayPal did not return a vault token for the card.", 502);
            }

            var vaultedCard = token.PaymentSource?.Card;
            return new VaultedCard(
                VaultId: token.Id,
                Brand: vaultedCard?.Brand?.Value,
                Last4: vaultedCard?.LastDigits,
                Expiry: vaultedCard?.Expiry,
                Name: vaultedCard?.Name);
        }, ct);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        return Invoke("delete-vault-card", async () =>
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }, ct);
    }

    public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return Invoke("search-transactions", async () =>
        {
            var results = new List<PayPalTransaction>();

            // Cover the whole range: chunk into <=31-day sub-windows, then page each window fully.
            for (var windowStart = from; windowStart < to; windowStart = windowStart.Add(MaxSearchWindow))
            {
                var windowEnd = windowStart.Add(MaxSearchWindow);
                if (windowEnd > to)
                {
                    windowEnd = to;
                }

                var page = 1;
                int totalPages;
                do
                {
                    var response = await _client.TransactionSearch.SearchTransactions(
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

                    foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalTransaction(
                            TransactionId: info.TransactionId,
                            Status: info.TransactionStatus,
                            Amount: ParseMoney(info.TransactionAmount),
                            CurrencyCode: info.TransactionAmount?.CurrencyCode,
                            Fee: ParseMoney(info.FeeAmount),
                            InvoiceId: info.InvoiceId,
                            CustomField: info.CustomField,
                            ReferenceId: info.PaypalReferenceId,
                            ReferenceIdType: info.PaypalReferenceIdType?.Value,
                            InitiationDate: null));
                    }

                    totalPages = response.TotalPages ?? 1;
                    page++;
                }
                while (page <= totalPages);
            }

            return (IReadOnlyList<PayPalTransaction>)results;
        }, ct);
    }

    // --- helpers -----------------------------------------------------------------------

    private CardRequest BuildCardRequest(CardDetails card) => new CardRequest
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.Line1,
            AddressLine2 = address.Line2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private void EnsureNoChallenge(OrderStatus? status)
    {
        if (status is not null && status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this payment in a browser. " +
                "Browser-approval (3DS) card payments are not supported by this integration.");
        }
    }

    private string FormatAmount(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "-0000";

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (decimal?)null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
    }

    private async Task<T> Invoke<T>(string operation, Func<Task<T>> call, CancellationToken ct)
    {
        var box = PayPalResponseContext.Begin();
        try
        {
            return await call();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentProcessorException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body, or an error body that didn't match its generated error shape.
            // The captured status separates a provider rejection (4xx) from an unreadable success (5xx).
            throw TranslateFailure(ex, operation, box);
        }
        catch (Exception ex)
        {
            throw TranslateFailure(ex, operation, box);
        }
    }

    private async Task Invoke(string operation, Func<Task> call, CancellationToken ct)
    {
        await Invoke<bool>(operation, async () =>
        {
            await call();
            return true;
        }, ct);
    }

    private PaymentProcessorException TranslateFailure(Exception ex, string operation, PayPalResponseContext.StatusBox box)
    {
        var capturedStatus = box.StatusCode;
        _logger.LogError(ex, "PayPal {Operation} failed (captured HTTP status {Status}). Provider body: {Body}",
            operation, capturedStatus, box.ErrorBody);

        if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new PaymentProcessorException("The payment provider is currently unreachable. Please try again.", 502, ex);
        }

        var status = capturedStatus;
        return status switch
        {
            400 => new PaymentProcessorException("The payment request was rejected as invalid by the provider.", 400, ex),
            404 => new PaymentProcessorException("The payment or resource was not found at the provider.", 404, ex),
            409 => new PaymentProcessorException("The payment is in a state that conflicts with this operation.", 409, ex),
            422 => new PaymentProcessorException("The payment could not be processed as requested.", 422, ex),
            _ => new PaymentProcessorException("The payment provider returned an unexpected response.", 502, ex)
        };
    }
}

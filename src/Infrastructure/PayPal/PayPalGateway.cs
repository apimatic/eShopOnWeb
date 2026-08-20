using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single seam between the application and PayPal. Wraps the PayPal .NET SDK, formats amounts to the
/// cent, passes a PayPal-Request-Id idempotency key on every write, and translates every SDK failure into a
/// <see cref="PayPalException"/> carrying the provider's HTTP status.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;

    public PayPalGateway(PayPalServerSdkClient client, PayPalSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    private string Currency => _settings.Currency ?? "USD";

    public async Task<AuthorizationResult> AuthorizeAsync(
        decimal amount, CardPaymentSource source, string invoiceId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        ReferenceId = invoiceId,
                        InvoiceId = invoiceId,
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = Currency,
                            Value = FormatAmount(amount)
                        }
                    }
                },
                PaymentSource = BuildOrderPaymentSource(source)
            };

            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}-create",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderRequest,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Build("Create PayPal order", e, ex);
            }

            // A browser (3-D Secure) challenge — stop and report; never build an approval round-trip.
            if (IsPayerActionRequired(created.Status))
            {
                return new AuthorizationResult(created.Id ?? string.Empty, null, null, created.Status?.ToString() ?? string.Empty, true);
            }

            // With a card payment source and intent=AUTHORIZE, PayPal authorizes the hold at CreateOrder time,
            // so the authorization is already on the create response. Only authorize explicitly when it isn't.
            var authorization = created.PurchaseUnits is { Count: > 0 } ? FirstAuthorization(created.PurchaseUnits) : null;
            var finalStatus = created.Status;

            if (authorization is null)
            {
                OrderAuthorizeResponse auth;
                try
                {
                    auth = await _client.Orders.AuthorizeOrder(
                        id: created.Id,
                        payPalMockResponse: null,
                        payPalRequestId: $"{idempotencyKey}-authorize",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        ct: cancellationToken);
                }
                catch (SdkException<AuthorizeOrderError> ex)
                {
                    ex.Error.TryGetError(out var e);
                    throw Build("Authorize PayPal order", e, ex);
                }

                if (IsPayerActionRequired(auth.Status))
                {
                    return new AuthorizationResult(created.Id ?? string.Empty, null, null, auth.Status?.ToString() ?? string.Empty, true);
                }

                authorization = auth.PurchaseUnits is { Count: > 0 } ? FirstAuthorization(auth.PurchaseUnits) : null;
                finalStatus = auth.Status ?? created.Status;
            }

            return new AuthorizationResult(
                PayPalOrderId: created.Id ?? string.Empty,
                AuthorizationId: authorization?.Id,
                AuthorizationStatus: authorization?.Status?.ToString(),
                OrderStatus: finalStatus?.ToString() ?? string.Empty,
                RequiresBuyerApproval: false);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Authorize", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Authorize", ex); }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            CapturedPayment captured;
            try
            {
                captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Build("Capture payment", e, ex);
            }

            var breakdown = captured.SellerReceivableBreakdown;
            var capturedAmount = ParseMoney(captured.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m;
            var currency = captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? Currency;

            return new CaptureResult(
                CaptureId: captured.Id ?? string.Empty,
                Status: captured.Status?.ToString() ?? string.Empty,
                CapturedAmount: capturedAmount,
                PayPalFee: ParseMoney(breakdown?.PaypalFee),
                NetAmount: ParseMoney(breakdown?.NetAmount),
                CurrencyCode: currency);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Capture", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Capture", ex); }
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            PaymentAuthorization resp;
            try
            {
                resp = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) }
                    },
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Build("Renew authorization", e, ex);
            }

            return new ReauthorizeResult(resp.Id ?? string.Empty, resp.Status?.ToString() ?? string.Empty);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Reauthorize", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Reauthorize", ex); }
    }

    public async Task<VoidResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            PaymentAuthorization resp;
            try
            {
                resp = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Build("Void authorization", e, ex);
            }

            return new VoidResult(resp?.Status?.ToString() ?? "VOIDED");
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Void", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Void", ex); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            // No invoice id on the refund: the capture already carries the order's unique invoice, and a
            // reused invoice id would trip the account's duplicate-invoice guard on partial refunds.
            var body = amount is { } value
                ? new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(value) } }
                : new RefundRequest();

            Refund resp;
            try
            {
                resp = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                ex.Error.TryGetError(out var e);
                throw Build("Refund payment", e, ex);
            }

            var refundAmount = ParseMoney(resp.Amount) ?? amount ?? 0m;
            return new RefundResult(resp.Id ?? string.Empty, resp.Status?.ToString() ?? string.Empty, refundAmount);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Refund", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Refund", ex); }
    }

    public async Task<VaultCardResult> VaultCardAsync(
        CardDetails card, string merchantCustomerId, string? existingCustomerId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            var customer = existingCustomerId is not null
                ? new Customer { Id = existingCustomerId }
                : new Customer { MerchantCustomerId = merchantCustomerId };

            var request = new PaymentTokenRequest
            {
                Customer = customer,
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        Name = card.CardholderName,
                        BillingAddress = MapAddress(card.BillingAddress)
                    }
                }
            };

            PaymentTokenResponse resp;
            try
            {
                resp = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: request,
                    ct: cancellationToken);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                ex.Error.TryGetError1(out var e);
                throw Build("Save card", e, ex);
            }

            var cardEntity = resp.PaymentSource?.Card;
            return new VaultCardResult(
                PaymentTokenId: resp.Id ?? string.Empty,
                CustomerId: resp.Customer?.Id ?? existingCustomerId,
                CardBrand: cardEntity?.Brand?.ToString() ?? "UNKNOWN",
                LastFourDigits: cardEntity?.LastDigits ?? "****",
                Expiry: cardEntity?.Expiry,
                CardholderName: cardEntity?.Name ?? card.CardholderName);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Save card", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Save card", ex); }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        try
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: cancellationToken);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                ex.Error.TryGetError1(out var e);
                throw Build("Delete saved card", e, ex);
            }
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Delete saved card", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Delete saved card", ex); }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        using var scope = PayPalResponseContext.BeginScope();
        var records = new List<PayPalTransactionRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // PayPal caps a single transaction-search window at 31 days, so walk the whole range in windows.
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart.AddDays(31);
                if (windowEnd > to) windowEnd = to;

                var page = 1;
                int totalPages;
                do
                {
                    SearchResponse resp;
                    try
                    {
                        resp = await _client.TransactionSearch.SearchTransactions(
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
                            page: page,
                            ct: cancellationToken);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        throw Build("Search transactions", ex.Error, ex);
                    }

                    totalPages = resp.TotalPages ?? 1;
                    if (resp.TransactionDetails is not null)
                    {
                        foreach (var detail in resp.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            if (info?.TransactionId is null || !seen.Add(info.TransactionId)) continue;

                            records.Add(new PayPalTransactionRecord(
                                TransactionId: info.TransactionId,
                                Status: info.TransactionStatus,
                                Amount: ParseMoney(info.TransactionAmount),
                                CurrencyCode: info.TransactionAmount?.CurrencyCode,
                                InvoiceId: info.InvoiceId,
                                InitiationDate: ParseDate(info.TransactionInitiationDate)));
                        }
                    }

                    page++;
                }
                while (page <= totalPages);

                if (windowEnd >= to) break;
                windowStart = windowEnd;
            }

            return records;
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex) { throw MalformedResponse("Search transactions", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable("Search transactions", ex); }
    }

    // ---- payment-source construction ----

    private static PaymentSource BuildOrderPaymentSource(CardPaymentSource source)
    {
        if (source.IsVaulted)
        {
            return new PaymentSource { Card = new CardRequest { VaultId = source.VaultId } };
        }

        var card = source.Card!;
        return new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
    }

    private static SdkAddress? MapAddress(PayPalBillingAddress? address)
    {
        if (address is null) return null;
        return new SdkAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit> purchaseUnits)
    {
        foreach (var unit in purchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Count: > 0 })
            {
                return authorizations[0];
            }
        }
        return null;
    }

    private static bool IsPayerActionRequired(OrderStatus? status) =>
        status is not null &&
        string.Equals(status.ToString(), OrderStatus.PayerActionRequired.ToString(), StringComparison.OrdinalIgnoreCase);

    // ---- formatting / parsing ----

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'-0000'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    // ---- error translation ----

    private PayPalException Build(string operation, Error? error, Exception inner) =>
        Compose(operation, error?.Name, error?.Message, inner);

    private PayPalException Build(string operation, Error1? error, Exception inner) =>
        Compose(operation, error?.Name, error?.Message, inner);

    private PayPalException Build(string operation, RawError rawError, Exception inner)
    {
        var status = (int)rawError.StatusCode;
        string body;
        try { body = rawError.ReadAsString(); }
        catch { body = string.Empty; }
        var message = string.IsNullOrWhiteSpace(body) ? "no detail" : Truncate(body, 500);
        return new PayPalException($"{operation} failed (HTTP {status}): {message}", status, inner);
    }

    private PayPalException Compose(string operation, string? name, string? message, Exception inner)
    {
        var status = PayPalResponseContext.CurrentStatusCode;
        var detail = !string.IsNullOrWhiteSpace(message) ? message
            : !string.IsNullOrWhiteSpace(name) ? name
            : "no detail";

        // Surface PayPal's own issue detail (e.g. the details[] array) so the message is operator-actionable.
        var body = PayPalResponseContext.CurrentErrorBody;
        var issues = ExtractIssues(body);
        if (!string.IsNullOrWhiteSpace(issues))
        {
            detail = $"{detail} [{issues}]";
        }

        var prefix = status is int s ? $"{operation} failed (HTTP {s})" : $"{operation} failed";
        return new PayPalException($"{prefix}: {detail}", status, inner);
    }

    private static string? ExtractIssues(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("details", out var details) &&
                details.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var d in details.EnumerateArray())
                {
                    var issue = d.TryGetProperty("issue", out var i) ? i.GetString() : null;
                    var desc = d.TryGetProperty("description", out var de) ? de.GetString() : null;
                    var field = d.TryGetProperty("field", out var fe) ? fe.GetString() : null;
                    parts.Add(string.Join(" ", new[] { issue, field, desc }.Where(x => !string.IsNullOrWhiteSpace(x))));
                }
                if (parts.Count > 0) return Truncate(string.Join("; ", parts), 400);
            }
        }
        catch (System.Text.Json.JsonException) { /* not JSON — ignore */ }
        return null;
    }

    private static PayPalException MalformedResponse(string operation, Exception inner)
    {
        var status = PayPalResponseContext.CurrentStatusCode;
        // A 2xx with a broken body is genuinely unknown; a non-2xx whose body drifted was still a rejection.
        return new PayPalException($"{operation}: PayPal returned a response that could not be processed.", status, inner);
    }

    private static PayPalException Unreachable(string operation, Exception inner) =>
        new PayPalException($"{operation}: PayPal could not be reached.", 503, inner);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

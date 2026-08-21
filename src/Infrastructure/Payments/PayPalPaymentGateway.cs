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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const string PreferRepresentation = "return=representation";
    private const string BrowserChallengeMessage =
        "PayPal required a shopper to approve this card in a browser. This API does not support that challenge.";

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = BuildAuthorizeOrderRequest(orderId, amount, currency, BuildCardRequest(card), InvoiceId(orderId, idempotencyKey));
        return CreateAuthorizedOrderAsync(body, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = BuildAuthorizeOrderRequest(orderId, amount, currency, new CardRequest { VaultId = vaultId }, InvoiceId(orderId, idempotencyKey));
        return CreateAuthorizedOrderAsync(body, idempotencyKey, cancellationToken);
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(
                ct => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(auth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetAuthorizedPayment);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not read the PayPal payment hold.");
        }
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequest
        {
            Amount = MoneyOf(amount, currency)
        };

        try
        {
            var auth = await Write(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(auth);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetReauthorize, 409);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not renew the PayPal payment hold.");
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new CaptureRequest
        {
            Amount = MoneyOf(amount, currency),
            FinalCapture = true
        };

        try
        {
            var captured = await Write(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (captured.SellerReceivableBreakdown is null && captured.Id is not null)
            {
                captured = await _client.Payments.GetCapturedPayment(
                    captureId: captured.Id,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: cancellationToken);
            }

            return ToCaptureResult(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetCapture);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetCapturedPayment);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not capture the PayPal payment hold.");
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await Write(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetVoid);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not release the PayPal payment hold.");
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        RefundRequest? body = null;
        if (amount is not null)
        {
            body = new RefundRequest { Amount = MoneyOf(amount.Value, currency) };
        }

        try
        {
            var refund = await Write(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: $"{captureId}:{idempotencyKey}",
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (string.IsNullOrEmpty(refund.Id))
            {
                throw new PaymentException(502, "PayPal did not return a refund identifier.");
            }

            return new RefundResult(
                refund.Id,
                refund.Status?.Value,
                refund.Amount is null ? amount ?? 0m : PayPalMoneyFormatter.Parse(refund.Amount.Value));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetRefund);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not refund the PayPal capture.");
        }
    }

    public async Task<VaultedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = BuildVaultCard(card)
            },
            Customer = new Customer { MerchantCustomerId = merchantCustomerId }
        };

        try
        {
            var token = await Write(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: body,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (string.IsNullOrEmpty(token.Id))
            {
                throw new PaymentException(502, "PayPal did not return a saved-card identifier.");
            }

            var display = token.PaymentSource?.Card;
            var lastDigits = display?.LastDigits;
            if (string.IsNullOrWhiteSpace(lastDigits))
            {
                throw new PaymentException(502, "PayPal did not return a recognisable description of the saved card.");
            }

            return new VaultedCardResult(
                token.Id,
                lastDigits,
                display?.Brand?.Value,
                display?.Expiry,
                display?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapVaultError(ex.Error, TryGetCreatePaymentToken);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not save the card with PayPal.");
        }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Write(
                async ct =>
                {
                    await _client.Vault.DeletePaymentToken(
                        id: paymentTokenId,
                        requestOptions: null,
                        ct: ct);
                    return 0;
                },
                cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapVaultError(ex.Error, TryGetDeletePaymentToken);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not remove the saved card from PayPal.");
        }
    }

    public async Task<IReadOnlyList<ReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<ReportedTransaction>();
        foreach (var (start, end) in Chunk(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                SearchResponse response;
                try
                {
                    var pageCopy = page;
                    response = await Bounded(
                        ct => _client.TransactionSearch.SearchTransactions(
                            startDate: ToRfc3339(start),
                            endDate: ToRfc3339(end),
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
                            page: pageCopy,
                            requestOptions: null,
                            ct: ct),
                        cancellationToken);
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentException(
                        MapStatus(ex.Error.StatusCode),
                        "PayPal transaction search failed.");
                }
                catch (Exception ex) when (IsBoundary(ex))
                {
                    throw MapBoundary(ex, "Could not read PayPal transactions.");
                }

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new ReportedTransaction(
                            info.TransactionId,
                            info.InvoiceId,
                            info.CustomField,
                            info.TransactionStatus,
                            info.TransactionAmount?.Value,
                            info.FeeAmount?.Value,
                            info.TransactionAmount?.CurrencyCode,
                            info.TransactionInitiationDate,
                            info.PaypalReferenceId,
                            info.PaypalReferenceIdType?.Value));
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<AuthorizationResult> CreateAuthorizedOrderAsync(
        OrderRequest body,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await Write(
                ct => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            RejectBrowserChallenge(order.Status);

            if (string.IsNullOrEmpty(order.Id))
            {
                throw new PaymentException(502, "PayPal did not return an order identifier.");
            }

            var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization?.Id is null)
            {
                throw new PaymentException(502, "PayPal authorized the payment but did not return a hold identifier.");
            }

            return new AuthorizationResult(
                order.Id,
                order.Status?.Value,
                authorization.Id,
                authorization.Status?.Value,
                ParseTime(authorization.ExpirationTime),
                ParseTime(authorization.CreateTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapPaymentsError(ex.Error, TryGetCreateOrder);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw MapBoundary(ex, "Could not authorize the payment with PayPal.");
        }
    }

    private static OrderRequest BuildAuthorizeOrderRequest(
        int orderId,
        decimal amount,
        string currency,
        CardRequest card,
        string invoiceId)
    {
        var id = orderId.ToString(CultureInfo.InvariantCulture);
        return new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = PayPalMoneyFormatter.Format(amount, currency)
                    },
                    CustomId = id,
                    InvoiceId = invoiceId
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };
    }

    private static CardRequest BuildCardRequest(CardDetails card)
    {
        return new CardRequest
        {
            Number = NormalizePan(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode?.Trim(),
            Name = card.Name,
            BillingAddress = ToSdkAddress(card.BillingAddress)
        };
    }

    private static PaymentTokenRequestCard BuildVaultCard(CardDetails card)
    {
        return new PaymentTokenRequestCard
        {
            Number = NormalizePan(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode?.Trim(),
            Name = card.Name,
            BillingAddress = ToSdkAddress(card.BillingAddress)
        };
    }

    private static SdkAddress ToSdkAddress(BillingAddress? address)
    {
        return new SdkAddress
        {
            CountryCode = string.IsNullOrWhiteSpace(address?.CountryCode) ? "US" : address!.CountryCode.Trim(),
            AddressLine1 = address?.AddressLine1,
            AddressLine2 = address?.AddressLine2,
            AdminArea2 = address?.AdminArea2,
            AdminArea1 = address?.AdminArea1,
            PostalCode = address?.PostalCode
        };
    }

    private static string InvoiceId(int orderId, string idempotencyKey)
    {
        // PayPal requires invoice_id unique per merchant; custom_id carries the eShop order id for reconciliation.
        return $"ESHOP-{orderId}-{idempotencyKey}";
    }

    private static Money MoneyOf(decimal amount, string currency)
    {
        return new Money
        {
            CurrencyCode = currency,
            Value = PayPalMoneyFormatter.Format(amount, currency)
        };
    }

    private static string NormalizePan(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentException(400, "Card number is required.");
        }

        return new string(number.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new PaymentException(400, "Card expiry is required.");
        }

        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2 && parts[0].Length is 1 or 2 && parts[1].Length == 4)
        {
            return $"{parts[1]}-{parts[0].PadLeft(2, '0')}";
        }

        throw new PaymentException(400, "Card expiry must be YYYY-MM.");
    }

    private static void RejectBrowserChallenge(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentException(409, BrowserChallengeMessage);
        }
    }

    private static AuthorizationSnapshot ToSnapshot(PaymentAuthorization auth)
    {
        if (string.IsNullOrEmpty(auth.Id))
        {
            throw new PaymentException(502, "PayPal did not return a hold identifier.");
        }

        return new AuthorizationSnapshot(
            auth.Id,
            auth.Status?.Value,
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime));
    }

    private static CaptureResult ToCaptureResult(CapturedPayment captured)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PaymentException(502, "PayPal did not return a capture identifier.");
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = breakdown is not null
            ? PayPalMoneyFormatter.Parse(breakdown.GrossAmount.Value)
            : PayPalMoneyFormatter.Parse(captured.Amount?.Value);
        decimal? fee = breakdown?.PaypalFee is null ? null : PayPalMoneyFormatter.Parse(breakdown.PaypalFee.Value);
        decimal? net = breakdown?.NetAmount is null ? null : PayPalMoneyFormatter.Parse(breakdown.NetAmount.Value);

        return new CaptureResult(captured.Id, captured.Status?.Value, capturedAmount, fee, net);
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Chunk(DateTimeOffset from, DateTimeOffset to)
    {
        var start = from;
        while (start < to)
        {
            var end = start.AddDays(31);
            if (end > to)
            {
                end = to;
            }

            yield return (start, end);
            start = end;
        }

        if (from == to)
        {
            yield return (from, to);
        }
    }

    private static string ToRfc3339(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> Write<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        SingleSendGuard.BeginWrite();
        try
        {
            return await Bounded(call, cancellationToken);
        }
        finally
        {
            SingleSendGuard.EndWrite();
        }
    }

    private static bool IsBoundary(Exception ex)
    {
        return ex is JsonException or HttpRequestException or TaskCanceledException or DuplicateSendRefusedException
            or OperationCanceledException;
    }

    private static PaymentException MapBoundary(Exception ex, string fallback)
    {
        if (ex is DuplicateSendRefusedException)
        {
            return new PaymentException(503, "The payment request may already have reached PayPal. Check the order before retrying.");
        }

        if (ex is JsonException)
        {
            var status = LastStatusHandler.LastResponse.Value?.StatusCode;
            if (status is >= HttpStatusCode.OK and < HttpStatusCode.BadRequest)
            {
                return new PaymentException(503, "The payment provider returned a response that could not be processed.");
            }

            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                return new PaymentException(MapStatus(status.Value), "PayPal rejected the request.");
            }

            return new PaymentException(503, fallback);
        }

        if (ex is OperationCanceledException && ex is not TaskCanceledException)
        {
            throw ex;
        }

        return new PaymentException(503, fallback);
    }

    private delegate bool TryPaymentsError<TError>(TError error, out Error? typed, out RawError? noContent, out RawError? raw);

    private static PaymentException MapPaymentsError<TError>(
        TError error,
        TryPaymentsError<TError> tryGet,
        int typedStatus = 400)
    {
        if (tryGet(error, out var typed, out var noContent, out var raw))
        {
            if (typed is not null)
            {
                return new PaymentException(typedStatus, FormatError(typed));
            }

            if (noContent is not null)
            {
                return new PaymentException(503, "PayPal returned an unexpected error.");
            }

            if (raw is not null)
            {
                return new PaymentException(MapStatus(raw.StatusCode), "PayPal returned an error.");
            }
        }

        return new PaymentException(502, "PayPal returned an error.");
    }

    private static bool TryGetCreateOrder(CreateOrderError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        noContent = null;
        if (error.TryGetError(out var e))
        {
            typed = e;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetAuthorizedPayment(GetAuthorizedPaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetReauthorize(ReauthorizePaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetCapture(CaptureAuthorizedPaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetCapturedPayment(GetCapturedPaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetVoid(VoidPaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetRefund(RefundCapturedPaymentError error, out Error? typed, out RawError? noContent, out RawError? raw)
    {
        if (error.TryGetError(out var e))
        {
            typed = e;
            noContent = null;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetNoContent(out var n))
        {
            noContent = n;
            raw = null;
            return true;
        }

        noContent = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private delegate bool TryVaultError<TError>(TError error, out Error1? typed, out RawError? raw);

    private static PaymentException MapVaultError<TError>(
        TError error,
        TryVaultError<TError> tryGet)
    {
        if (tryGet(error, out var typed, out var raw))
        {
            if (typed is not null)
            {
                var status = typed.Name.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) ? 404 : 400;
                return new PaymentException(status, FormatError1(typed));
            }

            if (raw is not null)
            {
                return new PaymentException(MapStatus(raw.StatusCode), "PayPal returned an error.");
            }
        }

        return new PaymentException(502, "PayPal returned an error.");
    }

    private static bool TryGetCreatePaymentToken(CreatePaymentTokenError error, out Error1? typed, out RawError? raw)
    {
        if (error.TryGetError1(out var e))
        {
            typed = e;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static bool TryGetDeletePaymentToken(DeletePaymentTokenError error, out Error1? typed, out RawError? raw)
    {
        if (error.TryGetError1(out var e))
        {
            typed = e;
            raw = null;
            return true;
        }

        typed = null;
        if (error.TryGetRawError(out var r))
        {
            raw = r;
            return true;
        }

        raw = null;
        return false;
    }

    private static string FormatError(Error error)
    {
        var details = error.Details is null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
        if (string.IsNullOrEmpty(details))
        {
            return error.Message;
        }

        return $"{error.Message} ({details})";
    }

    private static string FormatError1(Error1 error)
    {
        var details = error.Details is null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
        if (string.IsNullOrEmpty(details))
        {
            return error.Message;
        }

        return $"{error.Message} ({details})";
    }

    private static int MapStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 400 and < 500)
        {
            return code is 401 or 403 ? 502 : code;
        }

        return 503;
    }
}

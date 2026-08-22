using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currencyCode,
        string invoiceId,
        string customId,
        CardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return AuthorizeAsync(amount, currencyCode, invoiceId, customId, BuildCardRequest(card), payPalRequestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currencyCode,
        string invoiceId,
        string customId,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var card = new CardRequest { VaultId = vaultId };
        return AuthorizeAsync(amount, currencyCode, invoiceId, customId, card, payPalRequestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
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

            return new PayPalAuthorizationDetails
            {
                AuthorizationId = auth.Id ?? authorizationId,
                Status = auth.Status?.Value ?? string.Empty,
                ExpirationTime = ParsePayPalTime(auth.ExpirationTime)
            };
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest { Amount = MoneyOf(amount, currencyCode) },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return new PayPalAuthorizationResult
            {
                PayPalOrderId = string.Empty,
                AuthorizationId = auth.Id ?? authorizationId,
                AuthorizationStatus = auth.Status?.Value ?? string.Empty,
                ExpirationTime = ParsePayPalTime(auth.ExpirationTime)
            };
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var captured = await Bounded(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = MoneyOf(amount, currencyCode),
                        FinalCapture = true
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (captured.SellerReceivableBreakdown is null && captured.Id is not null)
            {
                captured = await Bounded(
                    ct => _client.Payments.GetCapturedPayment(
                        captureId: captured.Id,
                        payPalMockResponse: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
            }

            return MapCapture(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var captured = await Bounded(
                ct => _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return MapCapture(captured);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
    }

    public async Task<PayPalVoidResult> VoidAsync(string authorizationId, string payPalRequestId, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return new PayPalVoidResult
            {
                AuthorizationId = auth.Id ?? authorizationId,
                Status = auth.Status?.Value ?? "VOIDED"
            };
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            var mapped = MapPaymentsError(ex.Error);
            if (mapped.Message.Contains("PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase)
                || mapped.Message.Contains("already voided", StringComparison.OrdinalIgnoreCase))
            {
                return new PayPalVoidResult
                {
                    AuthorizationId = authorizationId,
                    Status = "VOIDED"
                };
            }

            throw mapped;
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currencyCode,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            RefundRequest? body = null;
            if (amount.HasValue)
            {
                body = new RefundRequest { Amount = MoneyOf(amount.Value, currencyCode) };
            }

            var refund = await Bounded(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return new PayPalRefundResult
            {
                RefundId = refund.Id ?? string.Empty,
                Status = refund.Status?.Value ?? string.Empty,
                Amount = MoneyFormat.Parse(refund.Amount?.Value)
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error);
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        string merchantCustomerId,
        CardInput card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: new PaymentTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = BuildVaultCard(card)
                        }
                    },
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            var vaultCard = response.PaymentSource?.Card;
            return new PayPalVaultResult
            {
                PaymentTokenId = response.Id ?? string.Empty,
                PayPalCustomerId = response.Customer?.Id,
                LastDigits = vaultCard?.LastDigits,
                Brand = vaultCard?.Brand?.Value,
                Expiry = vaultCard?.Expiry,
                CardholderName = vaultCard?.Name
            };
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapVaultError(ex.Error);
        }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
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
            var mapped = MapVaultError(ex.Error);
            if (mapped.StatusCode == 404)
            {
                return;
            }

            throw mapped;
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? currencyCode,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransactionRecord>();
        foreach (var window in SplitWindows(from, to))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    var currentPage = page;
                    response = await Bounded(
                        ct => _client.TransactionSearch.SearchTransactions(
                            startDate: FormatDate(window.Start),
                            endDate: FormatDate(window.End),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: currencyCode,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "all",
                            balanceAffectingRecordsOnly: "Y",
                            pageSize: 100,
                            page: currentPage,
                            requestOptions: null,
                            ct: ct),
                        cancellationToken);
                }
                catch (SdkException<RawError> ex)
                {
                    var body = ex.Error.ReadAsString() ?? string.Empty;
                    if ((int)ex.Error.StatusCode == 404
                        || body.Contains("Data for the given start date is not available", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    throw FromRaw(ex.Error);
                }

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        results.Add(MapTransaction(detail));
                    }

                    if (response.TransactionDetails.Count == 0)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }

                totalPages = response.TotalPages ?? page;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currencyCode,
        string invoiceId,
        string customId,
        CardRequest card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        Order order;
        try
        {
            order = await Bounded(
                ct => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderRequest
                    {
                        Intent = CheckoutPaymentIntent.Authorize,
                        PurchaseUnits = new List<PurchaseUnitRequest>
                        {
                            new()
                            {
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currencyCode,
                                    Value = MoneyFormat.ToPayPalValue(amount, currencyCode)
                                },
                                InvoiceId = invoiceId,
                                CustomId = customId
                            }
                        }
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrderError(ex.Error);
        }

        RejectPayerAction(order.Status, order.Links);

        var paypalOrderId = order.Id ?? string.Empty;
        var authorization = FindAuthorization(order);
        if (authorization is null)
        {
            try
            {
                var authorized = await Bounded(
                    ct => _client.Orders.AuthorizeOrder(
                        id: order.Id ?? string.Empty,
                        payPalMockResponse: null,
                        payPalRequestId: payPalRequestId + "-authorize",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: new OrderAuthorizeRequest
                        {
                            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                        },
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                RejectPayerAction(authorized.Status, authorized.Links);
                authorization = FindAuthorization(authorized);
                paypalOrderId = authorized.Id ?? paypalOrderId;
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapAuthorizeOrderError(ex.Error);
            }
        }

        if (authorization is null)
        {
            throw new CheckoutException(502, "PayPal did not return an authorization for this payment.");
        }

        if (authorization.Status == AuthorizationStatus.Denied
            || authorization.Status == AuthorizationStatus.Voided)
        {
            throw new CheckoutException(400, $"PayPal did not hold funds for this payment ({authorization.Status?.Value}).");
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = paypalOrderId,
            AuthorizationId = authorization.Id ?? string.Empty,
            AuthorizationStatus = authorization.Status?.Value ?? string.Empty,
            ExpirationTime = ParsePayPalTime(authorization.ExpirationTime),
            OrderStatus = order.Status?.Value
        };
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(Order order)
    {
        if (order.PurchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in order.PurchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                return authorization;
            }
        }

        return null;
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(OrderAuthorizeResponse order)
    {
        if (order.PurchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in order.PurchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                return authorization;
            }
        }

        return null;
    }

    private static void RejectPayerAction(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw BrowserChallenge();
        }

        if (links is null)
        {
            return;
        }

        foreach (var link in links)
        {
            var rel = link.Rel ?? string.Empty;
            if (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)
                || rel.Contains("payer_action", StringComparison.OrdinalIgnoreCase))
            {
                throw BrowserChallenge();
            }
        }
    }

    private static CheckoutException BrowserChallenge() =>
        new(409, "This card payment requires a shopper to approve a challenge in a browser. This application does not collect that approval.");

    private static CardRequest BuildCardRequest(CardInput card)
    {
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static PaymentTokenRequestCard BuildVaultCard(CardInput card)
    {
        return new PaymentTokenRequestCard
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static Money MoneyOf(decimal amount, string currencyCode) => new()
    {
        CurrencyCode = currencyCode,
        Value = MoneyFormat.ToPayPalValue(amount, currencyCode)
    };

    private static PayPalCaptureResult MapCapture(CapturedPayment captured)
    {
        var breakdown = captured.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            CaptureId = captured.Id ?? string.Empty,
            Status = captured.Status?.Value ?? string.Empty,
            CapturedAmount = MoneyFormat.Parse(captured.Amount?.Value ?? breakdown?.GrossAmount?.Value),
            PaypalFee = breakdown?.PaypalFee is null ? null : MoneyFormat.Parse(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount is null ? null : MoneyFormat.Parse(breakdown.NetAmount.Value)
        };
    }

    private static PayPalTransactionRecord MapTransaction(TransactionDetails detail)
    {
        var info = detail.TransactionInfo;
        return new PayPalTransactionRecord
        {
            TransactionId = info?.TransactionId,
            PaypalReferenceId = info?.PaypalReferenceId,
            PaypalReferenceIdType = info?.PaypalReferenceIdType?.Value,
            TransactionEventCode = info?.TransactionEventCode,
            TransactionInitiationDate = ParsePayPalTime(info?.TransactionInitiationDate),
            TransactionUpdatedDate = ParsePayPalTime(info?.TransactionUpdatedDate),
            TransactionAmount = info?.TransactionAmount is null ? null : MoneyFormat.Parse(info.TransactionAmount.Value),
            CurrencyCode = info?.TransactionAmount?.CurrencyCode,
            FeeAmount = info?.FeeAmount is null ? null : MoneyFormat.Parse(info.FeeAmount.Value),
            TransactionStatus = info?.TransactionStatus,
            InvoiceId = info?.InvoiceId,
            CustomField = info?.CustomField,
            PaymentTrackingId = info?.PaymentTrackingId
        };
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitWindows(DateTimeOffset from, DateTimeOffset to)
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
            if (end == to)
            {
                yield break;
            }

            start = end;
        }

        if (start == to)
        {
            yield return (from, to);
        }
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParsePayPalTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        PayPalCallContext.ProtectWrites.Value = true;
        PayPalCallContext.WriteCount.Value = 0;
        try
        {
            return await call(cts.Token);
        }
        catch (DuplicatePayPalWriteException)
        {
            throw new CheckoutException(409, "The payment request was already sent to PayPal. Refresh the order before retrying.");
        }
        catch (JsonException ex)
        {
            var status = PayPalCallContext.LastStatusCode.Value;
            if (status is >= 400 and < 500)
            {
                throw new CheckoutException(status.Value, "PayPal rejected the request.", ex);
            }

            throw new CheckoutException(502, "PayPal returned a response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CheckoutException(502, "PayPal is unreachable.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new CheckoutException(504, "The PayPal request timed out.", ex);
        }
        finally
        {
            PayPalCallContext.ProtectWrites.Value = false;
            PayPalCallContext.WriteCount.Value = 0;
        }
    }

    private static CheckoutException MapCreateOrderError(CreateOrderError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not create the payment.");
    }

    private static CheckoutException MapAuthorizeOrderError(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not authorize the payment.");
    }

    private static CheckoutException MapPaymentsError(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, 409);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not capture the payment.");
    }

    private static CheckoutException MapPaymentsError(GetCapturedPaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not load the capture.");
    }

    private static CheckoutException MapPaymentsError(GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not load the authorization.");
    }

    private static CheckoutException MapPaymentsError(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(409, "PayPal could not renew the payment hold.");
    }

    private static CheckoutException MapPaymentsError(VoidPaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, 409);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not release the payment hold.");
    }

    private static CheckoutException MapPaymentsError(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var body))
        {
            return FromError(body, 409);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not refund the payment.");
    }

    private static CheckoutException MapVaultError(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var body))
        {
            return FromError1(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not save the card.");
    }

    private static CheckoutException MapVaultError(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var body))
        {
            return FromError1(body);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(raw);
        }

        return new CheckoutException(400, "PayPal could not remove the saved card.");
    }

    private static CheckoutException FromError(Error error, int fallbackStatus = 400)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(error.Message))
        {
            parts.Add(error.Message);
        }

        if (error.Details is not null)
        {
            foreach (var detail in error.Details)
            {
                var piece = string.IsNullOrWhiteSpace(detail.Description)
                    ? detail.Issue
                    : $"{detail.Issue}: {detail.Description}";
                if (!string.IsNullOrWhiteSpace(detail.Field))
                {
                    piece = $"{piece} (field={detail.Field})";
                }
                if (!string.IsNullOrWhiteSpace(piece))
                {
                    parts.Add(piece);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(error.DebugId))
        {
            parts.Add($"debug_id={error.DebugId}");
        }

        return new CheckoutException(StatusFromName(error.Name, fallbackStatus), string.Join(" ", parts));
    }

    private static CheckoutException FromError1(Error1 error, int fallbackStatus = 400)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(error.Message))
        {
            parts.Add(error.Message);
        }

        if (error.Details is not null)
        {
            foreach (var detail in error.Details)
            {
                var piece = string.IsNullOrWhiteSpace(detail.Description)
                    ? detail.Issue
                    : $"{detail.Issue}: {detail.Description}";
                if (!string.IsNullOrWhiteSpace(detail.Field))
                {
                    piece = $"{piece} (field={detail.Field})";
                }
                if (!string.IsNullOrWhiteSpace(piece))
                {
                    parts.Add(piece);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(error.DebugId))
        {
            parts.Add($"debug_id={error.DebugId}");
        }

        return new CheckoutException(StatusFromName(error.Name, fallbackStatus), string.Join(" ", parts));
    }

    private static CheckoutException FromRaw(RawError raw)
    {
        var body = raw.ReadAsString();
        if (!string.IsNullOrEmpty(body) && body.Length > 500)
        {
            body = body[..500];
        }

        var message = string.IsNullOrWhiteSpace(body)
            ? "PayPal returned an error."
            : body;
        return new CheckoutException((int)raw.StatusCode, message);
    }

    private static int StatusFromName(string? name, int fallback)
    {
        var value = name?.ToUpperInvariant() ?? string.Empty;
        if (value.Contains("NOT_AUTHORIZED") || value.Contains("AUTHENTICATION_FAILURE"))
        {
            return 401;
        }

        if (value.Contains("RESOURCE_NOT_FOUND") || value.Contains("NOT_FOUND"))
        {
            return 404;
        }

        if (value.Contains("INTERNAL"))
        {
            return 502;
        }

        return fallback;
    }
}

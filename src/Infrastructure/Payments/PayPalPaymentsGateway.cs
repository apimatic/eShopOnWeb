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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentsGateway : IPayPalPaymentsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentsGateway> _logger;

    public PayPalPaymentsGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentsGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return AuthorizeAsync(orderId, amount, currency, ToCardRequest(card), idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest { VaultId = vaultTokenId };
        return AuthorizeAsync(orderId, amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw Map(ex.Error, "get authorization");
            }
        }, cancellationToken);
    }

    public Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var body = new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    }
                };

                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw Map(ex.Error, "reauthorize");
            }
        }, cancellationToken);
    }

    public Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var body = new CaptureRequest
                {
                    FinalCapture = true,
                    InvoiceId = $"{invoiceId}-{Guid.NewGuid():N}"
                };

                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);

                var breakdown = captured.SellerReceivableBreakdown;
                return new PayPalCaptureResult
                {
                    CaptureId = captured.Id ?? throw Missing("capture id"),
                    Status = captured.Status?.Value,
                    CapturedAmount = ParseMoney(captured.Amount, ParseMoney(breakdown?.GrossAmount, 0m)),
                    PaypalFee = ParseMoneyOrNull(breakdown?.PaypalFee),
                    NetAmount = ParseMoneyOrNull(breakdown?.NetAmount)
                };
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw Map(ex.Error, "capture");
            }
        }, cancellationToken);
    }

    public Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw Map(ex.Error, "void");
            }
        }, cancellationToken);
    }

    public Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                RefundRequest? body = null;
                if (amount is decimal refundAmount)
                {
                    body = new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = FormatAmount(refundAmount)
                        }
                    };
                }

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);

                return new PayPalRefundResult
                {
                    RefundId = refund.Id ?? throw Missing("refund id"),
                    Status = refund.Status?.Value,
                    Amount = ParseMoney(refund.Amount, amount ?? 0m)
                };
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw Map(ex.Error, "refund");
            }
        }, cancellationToken);
    }

    public Task<PayPalVaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var customer = string.IsNullOrWhiteSpace(payPalCustomerId)
                    ? new Customer { MerchantCustomerId = merchantCustomerId }
                    : new Customer { MerchantCustomerId = merchantCustomerId, Id = payPalCustomerId };

                var body = new PaymentTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = ToVaultCard(card)
                    }
                };

                var token = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: body,
                    requestOptions: null,
                    ct: ct);

                var cardEntity = token.PaymentSource?.Card;
                return new PayPalVaultedCardResult
                {
                    VaultTokenId = token.Id ?? throw Missing("vault token id"),
                    PayPalCustomerId = token.Customer?.Id,
                    LastDigits = cardEntity?.LastDigits,
                    Brand = cardEntity?.Brand?.Value,
                    Expiry = cardEntity?.Expiry,
                    Name = cardEntity?.Name
                };
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw Map(ex.Error, "vault card");
            }
        }, cancellationToken);
    }

    public Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultTokenId, requestOptions: null, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw Map(ex.Error, "delete saved card");
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            var collected = new List<PayPalReportedTransaction>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var window in SplitDateRange(from, to))
            {
                int page = 1;
                int totalPages = 1;
                do
                {
                    SearchResponse response;
                    try
                    {
                        response = await _client.TransactionSearch.SearchTransactions(
                            startDate: FormatRfc3339(window.Start),
                            endDate: FormatRfc3339(window.End),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: null,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "all",
                            balanceAffectingRecordsOnly: "Y",
                            pageSize: 100,
                            page: page,
                            requestOptions: null,
                            ct: ct);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        throw MapRaw(ex.Error, "transaction search");
                    }

                    totalPages = response.TotalPages is int pages && pages > 0 ? pages : 1;
                    if (response.TransactionDetails is null)
                    {
                        page++;
                        continue;
                    }

                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        var txnId = info.TransactionId;
                        if (!string.IsNullOrEmpty(txnId) && !seen.Add(txnId))
                        {
                            continue;
                        }

                        collected.Add(new PayPalReportedTransaction
                        {
                            TransactionId = txnId,
                            PaypalReferenceId = info.PaypalReferenceId,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            Status = info.TransactionStatus,
                            Amount = info.TransactionAmount?.Value,
                            FeeAmount = info.FeeAmount?.Value,
                            Currency = info.TransactionAmount?.CurrencyCode,
                            InitiationDate = info.TransactionInitiationDate,
                            PaymentMethodType = info.PaymentMethodType
                        });
                    }

                    page++;
                } while (page <= totalPages);
            }

            return (IReadOnlyList<PayPalReportedTransaction>)collected;
        }, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        CardRequest cardRequest,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var money = FormatAmount(amount);
        _logger.LogInformation("Creating PayPal authorization for eShop order {OrderId} amount {Amount} {Currency}",
            orderId, money, currency);

        var created = await Bounded(async ct =>
        {
            try
            {
                var body = new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = money
                            },
                            InvoiceId = InvoiceId(orderId),
                            CustomId = $"ESHOP-{orderId}",
                            ReferenceId = "default",
                            Description = $"eShopOnWeb order {orderId}"
                        }
                    }
                };

                return await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"create-order-{orderId}-{Guid.NewGuid():N}",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw Map(ex.Error, "create PayPal order");
            }
        }, cancellationToken);

        StopIfPayerActionRequired(created.Status, created.Id);

        var paypalOrderId = created.Id ?? throw Missing("PayPal order id");

        var authorized = await Bounded(async ct =>
        {
            try
            {
                var body = new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = cardRequest
                    }
                };

                return await _client.Orders.AuthorizeOrder(
                    id: paypalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw Map(ex.Error, "authorize");
            }
        }, cancellationToken);

        StopIfPayerActionRequired(authorized.Status, authorized.Id);

        var authorization = authorized.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (authorization?.Id is null)
        {
            throw new PaymentException(
                "PayPal authorized the order but did not return an authorization id.",
                502);
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = authorized.Id ?? paypalOrderId,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status?.Value,
            Expiration = ParseTimestamp(authorization.ExpirationTime),
            Amount = authorization.Amount?.Value
        };
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        PayPalCallContext.Begin();
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (PayPalDuplicateSendException ex)
        {
            throw new PaymentException(
                "The payment request may already have reached PayPal. Refresh order state before retrying.",
                502,
                innerException: ex);
        }
        catch (JsonException ex)
        {
            var status = PayPalCallContext.LastStatusNumber;
            if (status is >= 400)
            {
                throw new PaymentException(
                    "PayPal rejected the request.",
                    status.Value,
                    innerException: ex);
            }

            throw new PaymentException(
                "The provider returned a response that could not be processed.",
                502,
                innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentException("PayPal authentication could not be applied.", 502, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new PaymentException("PayPal is unreachable.", 502, innerException: ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken cancellationToken) =>
        await Bounded(async ct =>
        {
            await call(ct);
            return true;
        }, cancellationToken);

    private static void StopIfPayerActionRequired(OrderStatus? status, string? paypalOrderId)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentException(
                $"PayPal requires a shopper browser challenge (PAYER_ACTION_REQUIRED) for order {paypalOrderId}. This integration does not collect a browser approval.",
                409)
            {
                IsBrowserChallenge = true
            };
        }
    }

    private static CardRequest ToCardRequest(CardPaymentDetails card)
    {
        return new CardRequest
        {
            Number = SanitizePan(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : MapAddress(card.BillingAddress)
        };
    }

    private static PaymentTokenRequestCard ToVaultCard(CardPaymentDetails card)
    {
        return new PaymentTokenRequestCard
        {
            Number = SanitizePan(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : MapAddress(card.BillingAddress)
        };
    }

    private static Address MapAddress(CardBillingAddress billing)
    {
        return new Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.AdminArea2,
            AdminArea1 = billing.AdminArea1,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static string SanitizePan(string number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static PayPalAuthorizationSnapshot ToSnapshot(PaymentAuthorization auth) => new()
    {
        AuthorizationId = auth.Id ?? throw Missing("authorization id"),
        Status = auth.Status?.Value,
        Expiration = ParseTimestamp(auth.ExpirationTime),
        CreateTime = ParseTimestamp(auth.CreateTime)
    };

    private static PaymentException Map(CreateOrderError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(AuthorizeOrderError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(GetAuthorizedPaymentError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 401, operation);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(ReauthorizePaymentError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(CaptureAuthorizedPaymentError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(VoidPaymentError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 409, operation);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(RefundCapturedPaymentError error, string operation)
    {
        if (error.TryGetError(out Error body))
        {
            return FromError(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return MapRaw(noContent, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(CreatePaymentTokenError error, string operation)
    {
        if (error.TryGetError1(out Error1 body))
        {
            return FromError1(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException Map(DeletePaymentTokenError error, string operation)
    {
        if (error.TryGetError1(out Error1 body))
        {
            return FromError1(body, PayPalCallContext.LastStatusNumber ?? 400, operation);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return MapRaw(raw, operation);
        }

        return new PaymentException($"PayPal {operation} failed.", PayPalCallContext.LastStatusNumber ?? 502);
    }

    private static PaymentException FromError(Error error, int statusCode, string operation)
    {
        var issues = error.Details?
            .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList() ?? new List<string>();
        var message = $"PayPal {operation} failed ({error.Name}): {error.Message}";
        if (issues.Count > 0)
        {
            message += " " + string.Join("; ", issues);
        }

        if (!string.IsNullOrEmpty(error.DebugId))
        {
            message += $" debug_id={error.DebugId}";
        }

        return new PaymentException(message, statusCode, error.Name, error.DebugId, issues);
    }

    private static PaymentException FromError1(Error1 error, int statusCode, string operation)
    {
        var issues = error.Details?
            .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList() ?? new List<string>();
        var message = $"PayPal {operation} failed ({error.Name}): {error.Message}";
        if (issues.Count > 0)
        {
            message += " " + string.Join("; ", issues);
        }

        if (!string.IsNullOrEmpty(error.DebugId))
        {
            message += $" debug_id={error.DebugId}";
        }

        return new PaymentException(message, statusCode, error.Name, error.DebugId, issues);
    }

    private static PaymentException MapRaw(RawError raw, string operation)
    {
        var body = raw.ReadAsString();
        var message = $"PayPal {operation} failed with HTTP {(int)raw.StatusCode}.";
        if (!string.IsNullOrWhiteSpace(body))
        {
            message += " " + TrimForCaller(body);
        }

        return new PaymentException(message, (int)raw.StatusCode);
    }

    private static string TrimForCaller(string body)
    {
        var trimmed = body.Replace('\n', ' ').Replace('\r', ' ');
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static string FormatAmount(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money? money, decimal fallback)
    {
        var parsed = ParseMoneyOrNull(money);
        return parsed ?? fallback;
    }

    private static decimal? ParseMoneyOrNull(Money? money)
    {
        if (string.IsNullOrWhiteSpace(money?.Value))
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitDateRange(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var cursor = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        if (end < cursor)
        {
            yield break;
        }

        var max = TimeSpan.FromDays(31);
        if (cursor == end)
        {
            yield return (cursor, end);
            yield break;
        }

        while (cursor < end)
        {
            var windowEnd = cursor + max;
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            yield return (cursor, windowEnd);
            if (windowEnd == end)
            {
                yield break;
            }

            cursor = windowEnd;
        }
    }

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static InvalidOperationException Missing(string what) =>
        new($"PayPal did not return a {what}.");
}

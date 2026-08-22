using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(120);

    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<VaultedCardResult> VaultCardAsync(VaultCardCommand command, string idempotencyKey, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                var customer = string.IsNullOrEmpty(command.PayPalCustomerId)
                    ? new Customer { MerchantCustomerId = command.ShopperId }
                    : new Customer { Id = command.PayPalCustomerId };

                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: new PaymentTokenRequest
                    {
                        Customer = customer,
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Name = command.Card.Name,
                                Number = NormalizePan(command.Card.Number),
                                Expiry = command.Card.Expiry,
                                SecurityCode = command.Card.SecurityCode,
                                BillingAddress = ToPayPalAddress(command.Card.BillingAddress)
                            }
                        }
                    },
                    ct: token);

                var card = response.PaymentSource?.Card;
                return new VaultedCardResult(
                    PaymentTokenId: response.Id ?? throw new CheckoutException(502, "PayPal did not return a saved-card identifier."),
                    PayPalCustomerId: response.Customer?.Id,
                    LastDigits: card?.LastDigits,
                    Brand: card?.Brand?.Value,
                    Expiry: card?.Expiry,
                    Name: card?.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw MapVault(ex.Error);
            }
        }, ct);
    }

    public Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: token);
                return 0;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw MapVaultDelete(ex.Error);
            }
        }, ct);
    }

    public Task<AuthorizationResult> AuthorizePaymentAsync(AuthorizeCommand command, string createIdempotencyKey, string authorizeIdempotencyKey, CancellationToken ct)
    {
        return ReadAsync(async token =>
        {
            try
            {
                var created = await Once(() => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: createIdempotencyKey,
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
                                    CurrencyCode = command.Currency,
                                    Value = MoneyFormat.ToValue(command.Amount)
                                },
                                InvoiceId = command.InvoiceId,
                                CustomId = command.ShopperOrderId
                            }
                        }
                    },
                    prefer: "return=representation",
                    ct: token));

                if (created.Status == OrderStatus.PayerActionRequired)
                    return PayerAction(created.Id);

                var authorized = await Once(() => _client.Orders.AuthorizeOrder(
                    id: created.Id ?? throw new CheckoutException(502, "PayPal did not return an order id."),
                    payPalMockResponse: null,
                    payPalRequestId: authorizeIdempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource
                        {
                            Card = ToCardRequest(command.Card, command.VaultId)
                        }
                    },
                    prefer: "return=representation",
                    ct: token));

                if (authorized.Status == OrderStatus.PayerActionRequired)
                    return PayerAction(authorized.Id ?? created.Id);

                var auth = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()
                    ?? throw new CheckoutException(502, "PayPal did not return an authorization for this order.");

                return new AuthorizationResult(
                    PayPalOrderId: authorized.Id ?? created.Id!,
                    AuthorizationId: auth.Id ?? throw new CheckoutException(502, "PayPal did not return an authorization id."),
                    Status: auth.Status?.Value ?? string.Empty,
                    AmountValue: auth.Amount?.Value ?? MoneyFormat.ToValue(command.Amount),
                    Currency: auth.Amount?.CurrencyCode ?? command.Currency,
                    ExpirationTime: auth.ExpirationTime,
                    PayerActionRequired: false);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw MapOrders(ex.Error);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapAuthorize(ex.Error);
            }
        }, ct);
    }

    public Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        return ReadAsync(async token =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: token);
                return ToSnapshot(auth);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw MapGetAuthorization(ex.Error);
            }
        }, ct);
    }

    public Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = MoneyFormat.ToValue(amount)
                        }
                    },
                    prefer: "return=representation",
                    ct: token);
                return ToSnapshot(auth);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw MapReauthorize(ex.Error);
            }
        }, ct);
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        FinalCapture = true,
                        InvoiceId = invoiceId
                    },
                    prefer: "return=representation",
                    ct: token);

                if (captured.Status == CaptureStatus.Pending || captured.SellerReceivableBreakdown is null)
                {
                    if (!string.IsNullOrEmpty(captured.Id))
                    {
                        captured = await _client.Payments.GetCapturedPayment(
                            captureId: captured.Id,
                            payPalMockResponse: null,
                            ct: token);
                    }
                }

                var breakdown = captured.SellerReceivableBreakdown;
                return new CaptureResult(
                    CaptureId: captured.Id ?? throw new CheckoutException(502, "PayPal did not return a capture id."),
                    Status: captured.Status?.Value ?? string.Empty,
                    CapturedAmount: breakdown?.GrossAmount?.Value ?? captured.Amount?.Value,
                    PaypalFee: breakdown?.PaypalFee?.Value,
                    NetAmount: breakdown?.NetAmount?.Value,
                    Currency: captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw MapCapture(ex.Error);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw MapGetCapture(ex.Error);
            }
        }, ct);
    }

    public Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    ct: token);
                return 0;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw MapVoid(ex.Error);
            }
        }, ct);
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        return WriteAsync(async token =>
        {
            try
            {
                RefundRequest? body = amount is null
                    ? null
                    : new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = MoneyFormat.ToValue(amount.Value)
                        }
                    };

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token);

                return new RefundResult(
                    RefundId: refund.Id ?? throw new CheckoutException(502, "PayPal did not return a refund id."),
                    Status: refund.Status?.Value ?? string.Empty,
                    AmountValue: refund.Amount?.Value ?? (amount is null ? string.Empty : MoneyFormat.ToValue(amount.Value)),
                    Currency: refund.Amount?.CurrencyCode ?? currency);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw MapRefund(ex.Error);
            }
        }, ct);
    }

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return ReadAsync(async token =>
        {
            if (to < from)
                (from, to) = (to, from);

            var results = new List<PayPalTransactionRecord>();
            var windowStart = from;
            while (windowStart <= to)
            {
                var windowEnd = windowStart.AddDays(31).AddSeconds(-1);
                if (windowEnd > to || windowEnd < windowStart)
                    windowEnd = to;

                await SearchWindow(windowStart, windowEnd, results, token);

                if (windowEnd >= to)
                    break;
                windowStart = windowEnd.AddSeconds(1);
            }

            return (IReadOnlyList<PayPalTransactionRecord>)results;
        }, ct, SearchBudget);
    }

    private async Task SearchWindow(DateTimeOffset from, DateTimeOffset to, List<PayPalTransactionRecord> results, CancellationToken token)
    {
        var page = 1;
        int totalPages;
        do
        {
            try
            {
                var response = await _client.TransactionSearch.SearchTransactions(
                    startDate: Rfc3339(from),
                    endDate: Rfc3339(to),
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
                    ct: token);

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    results.Add(new PayPalTransactionRecord(
                        TransactionId: info?.TransactionId,
                        PaypalReferenceId: info?.PaypalReferenceId,
                        PaypalReferenceIdType: info?.PaypalReferenceIdType?.Value,
                        TransactionEventCode: info?.TransactionEventCode,
                        TransactionInitiationDate: info?.TransactionInitiationDate is null ? null : $"{info.TransactionInitiationDate}",
                        TransactionAmount: AmountValue(info?.TransactionAmount),
                        FeeAmount: AmountValue(info?.FeeAmount),
                        TransactionStatus: info?.TransactionStatus,
                        InvoiceId: info?.InvoiceId,
                        CustomField: info?.CustomField));
                }

                totalPages = response.TotalPages ?? page;
                page++;
            }
            catch (SdkException<RawError> ex)
            {
                var body = ex.Error.ReadAsString() ?? string.Empty;
                if (body.Contains("not available", StringComparison.OrdinalIgnoreCase))
                    return;
                throw FromRaw(ex.Error);
            }
        } while (page <= totalPages);
    }

    private static string? AmountValue(Money? money) => money?.Value;

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static AuthorizationResult PayerAction(string? paypalOrderId) =>
        new(paypalOrderId ?? string.Empty, string.Empty, OrderStatus.PayerActionRequired.Value, "0.00", string.Empty, null, true);

    private static AuthorizationSnapshot ToSnapshot(PaymentAuthorization auth) =>
        new(
            AuthorizationId: auth.Id ?? string.Empty,
            Status: auth.Status?.Value ?? string.Empty,
            AmountValue: auth.Amount?.Value,
            ExpirationTime: auth.ExpirationTime);

    private static CardRequest ToCardRequest(CardPaymentSource? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
        }

        // Omit Attributes: AvsCvv is a validity hold, not an authorize field (PayPal 400
        // INVALID_PARAMETER_VALUE). CardVerification.Method also defaults to ScaWhenRequired
        // and always serializes, so a Verification object would emit SCA.
        return new CardRequest
        {
            Name = card!.Name,
            Number = NormalizePan(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress ToPayPalAddress(BillingAddressInfo? address) =>
        new()
        {
            CountryCode = string.IsNullOrWhiteSpace(address?.CountryCode) ? "US" : address!.CountryCode,
            AddressLine1 = address?.AddressLine1 ?? "123 Main St.",
            AddressLine2 = address?.AddressLine2,
            AdminArea2 = address?.AdminArea2 ?? "Kent",
            AdminArea1 = address?.AdminArea1 ?? "OH",
            PostalCode = address?.PostalCode ?? "44240"
        };

    private static string NormalizePan(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static async Task<T> Once<T>(Func<Task<T>> call)
    {
        using (SingleSendScope.Enter())
            return await call();
    }

    private Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        Bounded(operation, ct, CallBudget, write: true);

    private Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct, TimeSpan? budget = null) =>
        Bounded(operation, ct, budget ?? CallBudget, write: false);

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct, TimeSpan budget, bool write)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        using var scope = write ? SingleSendScope.Enter() : null;
        try
        {
            return await operation(cts.Token);
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (DuplicateWritePreventedException)
        {
            throw new CheckoutException(409, "The payment request may already have been sent to PayPal. Refresh the order and retry if needed.");
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var status = ex is TaskCanceledException && !ct.IsCancellationRequested ? 504 : 502;
            throw new CheckoutException(status, "The payment provider is unreachable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new CheckoutException(502, "PayPal authentication failed.", ex);
        }
    }

    private static CheckoutException MapJsonException(JsonException ex)
    {
        var status = PayPalStatusCaptureHandler.LastHttpStatus;
        if (status is >= 400 and < 500)
            return new CheckoutException(400, "PayPal rejected the request.", ex);
        return new CheckoutException(502, "The payment provider returned a response that could not be processed.", ex);
    }

    private static CheckoutException MapOrders(CreateOrderError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 400);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal rejected the order.");
    }

    private static CheckoutException MapAuthorize(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 400);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not authorize the payment.");
    }

    private static CheckoutException MapGetAuthorization(GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 404);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not load the authorization.");
    }

    private static CheckoutException MapReauthorize(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 409);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(409, "The authorization could not be renewed.");
    }

    private static CheckoutException MapCapture(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 409);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not capture the authorization.");
    }

    private static CheckoutException MapGetCapture(GetCapturedPaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 404);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not load the capture.");
    }

    private static CheckoutException MapVoid(VoidPaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 409);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not release the authorization.");
    }

    private static CheckoutException MapRefund(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var body)) return FromError(body, 409);
        if (error.TryGetNoContent(out var noContent)) return FromRaw(noContent);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not refund the capture.");
    }

    private static CheckoutException MapVault(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var body)) return FromError1(body, 400);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not save the card.");
    }

    private static CheckoutException MapVaultDelete(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var body)) return FromError1(body, 400);
        if (error.TryGetRawError(out var raw)) return FromRaw(raw);
        return new CheckoutException(502, "PayPal could not delete the saved card.");
    }

    private static CheckoutException FromError(Error body, int fallbackStatus)
    {
        var issues = body.Details?.Select(d => string.IsNullOrWhiteSpace(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}").ToList()
                     ?? new List<string>();
        return new CheckoutException(InferStatus(body.Name, fallbackStatus), ComposeMessage(body.Message, issues))
        {
            ProviderName = body.Name,
            ProviderDebugId = body.DebugId,
            Issues = issues
        };
    }

    private static CheckoutException FromError1(Error1 body, int fallbackStatus)
    {
        var issues = body.Details?.Select(d => string.IsNullOrWhiteSpace(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}").ToList()
                     ?? new List<string>();
        return new CheckoutException(InferStatus(body.Name, fallbackStatus), ComposeMessage(body.Message, issues))
        {
            ProviderName = body.Name,
            ProviderDebugId = body.DebugId,
            Issues = issues
        };
    }

    private static CheckoutException FromRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        if (status == 0) status = 502;
        var mapped = status is >= 400 and < 500 ? status : 502;
        if (status == 404) mapped = 404;
        if (status == 409) mapped = 409;
        var body = raw.ReadAsString();
        var message = string.IsNullOrWhiteSpace(body) ? "PayPal rejected the request." : "PayPal rejected the request.";
        return new CheckoutException(mapped, message)
        {
            ProviderDebugId = null,
            Issues = string.IsNullOrWhiteSpace(body) ? null : new[] { body.Length > 500 ? body[..500] : body }
        };
    }

    private static string ComposeMessage(string message, List<string> issues)
    {
        if (issues.Count == 0) return message;
        return $"{message} ({string.Join("; ", issues)})";
    }

    private static int InferStatus(string name, int fallback)
    {
        if (name.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RESOURCE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return 404;
        if (name.Contains("AUTHENTICATION", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
            return 502;
        if (name.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase))
            return 502;
        if (name.Contains("UNPROCESSABLE", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("INVALID", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DECLINED", StringComparison.OrdinalIgnoreCase))
            return 400;
        return fallback;
    }
}

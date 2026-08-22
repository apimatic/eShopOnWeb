using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var cardRequest = BuildRawCardRequest(card);
        return AuthorizeAsync(orderId, invoiceId, amount, currency, cardRequest, payPalRequestId, cancellationToken);
    }

    public Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        string vaultId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            VaultId = vaultId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };
        return AuthorizeAsync(orderId, invoiceId, amount, currency, cardRequest, payPalRequestId, cancellationToken);
    }

    public Task<CaptureProceeds> CaptureAsync(
        string authorizationId,
        string invoiceId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        FinalCapture = true,
                        InvoiceId = invoiceId
                    },
                    prefer: "return=representation",
                    ct: ct);

                return ReadCapture(capture);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw MapCaptureAuthorizedPaymentError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.Format(amount, currency)
                        }
                    },
                    prefer: "return=representation",
                    ct: ct);

                if (auth.Status == AuthorizationStatus.Denied || auth.Status == AuthorizationStatus.Voided)
                {
                    throw new PaymentException(
                        "PayPal could not renew the authorization. Ask the shopper to pay again so a new hold can be created.",
                        409);
                }

                return new AuthorizationHold(
                    PayPalOrderId: string.Empty,
                    AuthorizationId: auth.Id ?? throw MissingId("reauthorization"),
                    Status: auth.Status?.Value ?? string.Empty,
                    AmountValue: auth.Amount?.Value ?? PayPalMoney.Format(amount, currency),
                    Currency: auth.Amount?.CurrencyCode ?? currency,
                    ExpirationTime: auth.ExpirationTime);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw MapReauthorizePaymentError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task<string> VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: "return=representation",
                    ct: ct);
                return auth.Status?.Value ?? AuthorizationStatus.Voided.Value;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw MapVoidPaymentError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task<RefundProceeds> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                RefundRequest? body = null;
                if (amount.HasValue)
                {
                    body = new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.Format(amount.Value, currency)
                        }
                    };
                }

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct);

                return new RefundProceeds(
                    RefundId: refund.Id ?? throw MissingId("refund"),
                    Status: refund.Status?.Value ?? string.Empty,
                    AmountValue: refund.Amount?.Value ?? (amount.HasValue ? PayPalMoney.Format(amount.Value, currency) : "0.00"),
                    Currency: refund.Amount?.CurrencyCode ?? currency);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw MapRefundCapturedPaymentError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task<VaultedCard> SaveCardAsync(
        string merchantCustomerId,
        CardPaymentInput card,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: new PaymentTokenRequest
                    {
                        Customer = new Customer
                        {
                            MerchantCustomerId = SanitizeMerchantCustomerId(merchantCustomerId)
                        },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = BuildVaultCard(card)
                        }
                    },
                    ct: ct);

                StopIfPayerAction(response.Links);

                var cardEntity = response.PaymentSource?.Card;
                return new VaultedCard(
                    PayPalPaymentTokenId: response.Id ?? throw MissingId("payment token"),
                    Brand: cardEntity?.Brand?.Value,
                    LastDigits: cardEntity?.LastDigits,
                    Expiry: cardEntity?.Expiry,
                    CardholderName: cardEntity?.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw MapCreatePaymentTokenError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task DeleteSavedCardAsync(string payPalPaymentTokenId, CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: payPalPaymentTokenId, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw MapDeletePaymentTokenError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string currency,
        CancellationToken cancellationToken)
    {
        return Bounded(async ct =>
        {
            var collected = new List<PayPalReportedTransaction>();
            var windowStart = from;
            var maxWindow = TimeSpan.FromDays(31);

            while (windowStart <= to)
            {
                var windowEnd = windowStart + maxWindow;
                if (windowEnd > to)
                    windowEnd = to;

                await SearchWindow(windowStart, windowEnd, currency, collected, ct);

                if (windowEnd == to)
                    break;
                windowStart = windowEnd;
            }

            return (IReadOnlyList<PayPalReportedTransaction>)collected;
        }, cancellationToken);
    }

    private async Task<AuthorizationHold> AuthorizeAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardRequest cardRequest,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var created = await _client.Orders.CreateOrder(
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
                            new PurchaseUnitRequest
                            {
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    Value = PayPalMoney.Format(amount, currency)
                                },
                                CustomId = invoiceId,
                                InvoiceId = invoiceId,
                                Description = $"eShop order {orderId}"
                            }
                        },
                        PaymentSource = new PaymentSource
                        {
                            Card = cardRequest
                        }
                    },
                    prefer: "return=representation",
                    ct: ct);

                StopIfChallenge(created.Status, created.Links, created.PaymentSource?.Card);

                var hold = TryReadAuthorization(created.Id, created.PurchaseUnits, currency, amount);
                if (hold is not null)
                {
                    _logger.LogInformation("Authorized PayPal order {PayPalOrderId} hold {AuthorizationId} for eShop order {OrderId}", hold.PayPalOrderId, hold.AuthorizationId, orderId);
                    return hold;
                }

                var authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id ?? throw MissingId("PayPal order"),
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId + "-authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);

                StopIfChallenge(authorized.Status, authorized.Links, authorized.PaymentSource?.Card);

                hold = TryReadAuthorization(authorized.Id ?? created.Id, authorized.PurchaseUnits, currency, amount);
                if (hold is null)
                    throw new PaymentException("PayPal did not return an authorization for this payment.", 502);

                return hold;
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw MapCreateOrderError(ex);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapAuthorizeOrderError(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }
        }, cancellationToken);
    }

    private async Task SearchWindow(
        DateTimeOffset from,
        DateTimeOffset to,
        string currency,
        List<PayPalReportedTransaction> sink,
        CancellationToken ct)
    {
        var start = ToRfc3339(from);
        var end = ToRfc3339(to);

        for (var page = 1; ; page++)
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
                    transactionCurrency: currency,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "all",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRaw(ex.Error);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw MapBoundary(ex);
            }

            var details = response.TransactionDetails;
            if (details is not null)
            {
                foreach (var row in details)
                {
                    var info = row.TransactionInfo;
                    if (info is null)
                        continue;
                    sink.Add(new PayPalReportedTransaction(
                        TransactionId: info.TransactionId ?? string.Empty,
                        InvoiceId: info.InvoiceId,
                        CustomField: info.CustomField,
                        Status: info.TransactionStatus,
                        AmountValue: info.TransactionAmount?.Value,
                        FeeAmountValue: info.FeeAmount?.Value,
                        Currency: info.TransactionAmount?.CurrencyCode,
                        InitiationDate: info.TransactionInitiationDate,
                        PaypalReferenceId: info.PaypalReferenceId));
                }
            }

            var totalPages = response.TotalPages ?? page;
            var pageCount = details?.Count ?? 0;
            if (page >= totalPages || pageCount == 0)
                break;
        }
    }

    private static AuthorizationHold? TryReadAuthorization(
        string? payPalOrderId,
        IReadOnlyList<PurchaseUnit>? units,
        string currency,
        decimal amount)
    {
        var authorization = units?
            .SelectMany(u => u.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();
        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
            return null;

        if (authorization.Status == AuthorizationStatus.Denied)
            throw new PaymentException("PayPal declined the card authorization.", 402);

        return new AuthorizationHold(
            PayPalOrderId: payPalOrderId ?? string.Empty,
            AuthorizationId: authorization.Id,
            Status: authorization.Status?.Value ?? string.Empty,
            AmountValue: authorization.Amount?.Value ?? PayPalMoney.Format(amount, currency),
            Currency: authorization.Amount?.CurrencyCode ?? currency,
            ExpirationTime: authorization.ExpirationTime);
    }

    private static CaptureProceeds ReadCapture(CapturedPayment capture)
    {
        if (capture.Status == CaptureStatus.Declined || capture.Status == CaptureStatus.Failed)
            throw new PaymentException("PayPal declined or failed the capture. The operator should retry fulfilment or ask the shopper to pay again.", 409);

        var breakdown = capture.SellerReceivableBreakdown;
        var amount = breakdown?.GrossAmount?.Value ?? capture.Amount?.Value
            ?? throw new PaymentException("PayPal did not report a captured amount.", 502);

        return new CaptureProceeds(
            CaptureId: capture.Id ?? throw MissingId("capture"),
            Status: capture.Status?.Value ?? string.Empty,
            AmountValue: amount,
            Currency: breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? string.Empty,
            PaypalFeeValue: breakdown?.PaypalFee?.Value,
            NetAmountValue: breakdown?.NetAmount?.Value);
    }

    private static void StopIfChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links, CardResponse? card)
    {
        if (status == OrderStatus.PayerActionRequired)
            throw Challenge();
        StopIfPayerAction(links);

        var paRes = card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus;
        if (paRes == ParesStatus.C || paRes == ParesStatus.D || paRes == ParesStatus.R)
            throw Challenge();
    }

    private static void StopIfPayerAction(IReadOnlyList<LinkDescription>? links)
    {
        if (links is null)
            return;
        if (links.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)))
            throw Challenge();
    }

    private static PaymentException Challenge() =>
        new("PayPal required a shopper challenge that this integration does not support. The payment was not completed.", 409)
        {
            ChallengeRequired = true
        };

    private static CardRequest BuildRawCardRequest(CardPaymentInput card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = NormalizePan(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static PaymentTokenRequestCard BuildVaultCard(CardPaymentInput card)
    {
        return new PaymentTokenRequestCard
        {
            Name = card.Name,
            Number = NormalizePan(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static Address? ToAddress(CardBillingAddress? billing)
    {
        if (billing is null)
            return null;
        var country = string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode;
        return new Address
        {
            CountryCode = country,
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.AdminArea2,
            AdminArea1 = billing.AdminArea1,
            PostalCode = billing.PostalCode
        };
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var sanitized = Regex.Replace(buyerId ?? string.Empty, @"[^A-Za-z0-9_-]", "_");
        if (sanitized.Length > 64)
            sanitized = sanitized.Substring(0, 64);
        return string.IsNullOrEmpty(sanitized) ? "shopper" : sanitized;
    }

    private static string NormalizePan(string number) =>
        Regex.Replace(number ?? string.Empty, @"[\s-]", string.Empty);

    private static string NormalizeExpiry(string expiry)
    {
        var value = (expiry ?? string.Empty).Trim();
        if (Regex.IsMatch(value, @"^[0-9]{4}-(0[1-9]|1[0-2])$"))
            return value;

        var slash = Regex.Match(value, @"^(0[1-9]|1[0-2])[/\-]([0-9]{2}|[0-9]{4})$");
        if (slash.Success)
        {
            var month = slash.Groups[1].Value;
            var year = slash.Groups[2].Value;
            if (year.Length == 2)
                year = "20" + year;
            return $"{year}-{month}";
        }

        throw new PaymentException("Card expiry must be ISO YYYY-MM.");
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static PaymentException MissingId(string what) =>
        new($"PayPal did not return an id for the {what}.", 502) { OutcomeUnknown = true };

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException
        or AuthSchemeException or PaymentException;

    private static PaymentException MapBoundary(Exception ex)
    {
        if (ex is PaymentException payment)
            return payment;
        if (ex is JsonException)
        {
            var status = PayPalStatusCaptureHandler.LastStatus.Value;
            if (status.HasValue && (int)status.Value >= 400 && (int)status.Value < 500)
            {
                return new PaymentException("PayPal rejected the request.", (int)status.Value);
            }
            return new PaymentException("The payment provider returned a response that could not be processed.", 502)
            {
                OutcomeUnknown = true
            };
        }
        if (ex is AuthSchemeException)
            return new PaymentException("PayPal authentication is not configured correctly.", 502);
        return new PaymentException("The payment provider is unreachable. Try again shortly.", ex, 503);
    }

    private static PaymentException MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return FromError(error);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the authorization.", 400);
    }

    private static PaymentException MapAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return FromError(error);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the authorization.", 400);
    }

    private static PaymentException MapCaptureAuthorizedPaymentError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return FromError(error);
        if (ex.Error.TryGetNoContent(out var noContent))
            return MapRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the capture.", 400);
    }

    private static PaymentException MapReauthorizePaymentError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var mapped = FromError(error);
            return new PaymentException(
                "PayPal could not renew the authorization. Ask the shopper to pay again so a new hold can be created. " + mapped.Message,
                409)
            {
                ProviderName = mapped.ProviderName,
                ProviderDebugId = mapped.ProviderDebugId
            };
        }
        if (ex.Error.TryGetNoContent(out var noContent))
            return MapRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException(
            "PayPal could not renew the authorization. Ask the shopper to pay again so a new hold can be created.",
            409);
    }

    private static PaymentException MapVoidPaymentError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return FromError(error);
        if (ex.Error.TryGetNoContent(out var noContent))
            return MapRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the void.", 400);
    }

    private static PaymentException MapRefundCapturedPaymentError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return FromError(error);
        if (ex.Error.TryGetNoContent(out var noContent))
            return MapRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the refund.", 400);
    }

    private static PaymentException MapCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
            return FromError1(error);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the saved card.", 400);
    }

    private static PaymentException MapDeletePaymentTokenError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
            return FromError1(error);
        if (ex.Error.TryGetRawError(out var raw))
            return MapRaw(raw);
        return new PaymentException("PayPal rejected the saved-card deletion.", 400);
    }

    private static PaymentException FromError(Error error)
    {
        var status = StatusFromName(error.Name);
        var details = FormatDetails(error.Details?.Select(d => (d.Issue, d.Description, d.Field)));
        var message = string.IsNullOrWhiteSpace(details)
            ? $"PayPal rejected the request ({error.Name}). {error.Message}"
            : $"PayPal rejected the request ({error.Name}): {details}";
        if (!string.IsNullOrEmpty(error.DebugId))
            message += $" DebugId={error.DebugId}";
        return new PaymentException(message.Trim(), status)
        {
            ProviderName = error.Name,
            ProviderDebugId = error.DebugId
        };
    }

    private static PaymentException FromError1(Error1 error)
    {
        var status = StatusFromName(error.Name);
        var details = FormatDetails(error.Details?.Select(d => (d.Issue, d.Description, d.Field)));
        var message = string.IsNullOrWhiteSpace(details)
            ? $"PayPal rejected the request ({error.Name}). {error.Message}"
            : $"PayPal rejected the request ({error.Name}): {details}";
        if (!string.IsNullOrEmpty(error.DebugId))
            message += $" DebugId={error.DebugId}";
        return new PaymentException(message.Trim(), status)
        {
            ProviderName = error.Name,
            ProviderDebugId = error.DebugId
        };
    }

    private static string FormatDetails(IEnumerable<(string Issue, string? Description, string? Field)>? details)
    {
        if (details is null)
            return string.Empty;
        return string.Join("; ", details.Select(d =>
        {
            var piece = d.Issue;
            if (!string.IsNullOrEmpty(d.Field))
                piece += $" ({d.Field})";
            if (!string.IsNullOrEmpty(d.Description))
                piece += $": {d.Description}";
            return piece;
        }));
    }

    private static int StatusFromName(string? name)
    {
        if (string.Equals(name, "RESOURCE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return 404;
        if (string.Equals(name, "AUTHENTICATION_FAILURE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
            return 502;
        return 400;
    }

    private static PaymentException MapRaw(RawError raw)
    {
        var http = (int)raw.StatusCode;
        var clientStatus = http is 401 or 403 or >= 500 ? 502 : http == 0 ? 502 : http;
        return new PaymentException("PayPal rejected the request.", clientStatus);
    }

    private Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct) =>
        Bounded(async token =>
        {
            await call(token);
            return 0;
        }, ct);
}

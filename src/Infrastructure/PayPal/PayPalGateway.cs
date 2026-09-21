using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The application's only path to PayPal, implemented over the PayPal Server SDK. Every SDK call is
/// wrapped so that typed API errors, unreadable bodies, and transport failures all surface as a
/// single <see cref="PayPalException"/>. A per-call 30s deadline bounds the whole operation
/// (the SDK's own timeout is per-attempt only).
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 200;

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct)
    {
        using var deadline = Deadline(ct);

        var card = BuildCard(request);
        var orderBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = FormatAmount(request.Amount)
                    },
                    CustomId = request.OrderReference,
                    InvoiceId = request.InvoiceId,
                    Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        // Card supplied at create + intent=AUTHORIZE: PayPal authorizes during CreateOrder, so ask
        // for the full representation and read the authorization straight off the create response.
        var order = await Guarded<Order, CreateOrderError>("create order",
            () => _client.Orders.CreateOrder(null, request.CreateRequestId, null, null, null, orderBody,
                prefer: "return=representation", ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        var orderStatus = order.Status?.Value ?? "UNKNOWN";
        if (order.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayPalChallengeException(
                $"PayPal requires a browser approval (payer action / 3DS challenge) for order {order.Id}. " +
                "This integration does not perform an approval round-trip; use a card that authorizes without a challenge.");
        }

        var authorization = FirstAuthorization(order.PurchaseUnits);

        // Fallback: if the order was not auto-authorized at creation, authorize it explicitly.
        if (authorization?.Id is null)
        {
            var authResponse = await Guarded<OrderAuthorizeResponse, AuthorizeOrderError>("authorize order",
                () => _client.Orders.AuthorizeOrder(order.Id!, null, request.AuthorizeRequestId, null, null, null,
                    prefer: "return=representation", ct: deadline.Token),
                e => e.TryGetError(out var er) ? er : null);
            orderStatus = authResponse.Status?.Value ?? orderStatus;
            authorization = authResponse.PurchaseUnits?
                .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
                .FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PayPalException(
                $"PayPal did not return an authorization for order {order.Id} (order status {orderStatus}).");
        }

        return new PayPalAuthorizationResult(
            PayPalOrderId: order.Id!,
            AuthorizationId: authorization.Id,
            AuthorizationStatus: authorization.Status?.Value ?? "UNKNOWN",
            OrderStatus: orderStatus,
            ExpiresAt: ParseDate(authorization.ExpirationTime),
            TransactionTime: ParseDate(authorization.CreateTime));
    }

    public async Task<PayPalAuthorizationInfo?> GetOrderAuthorizationAsync(string payPalOrderId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var order = await Guarded<Order, GetOrderError>("get order",
            () => _client.Orders.GetOrder(payPalOrderId, null, null, null, ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        var authorization = order.PurchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (authorization?.Id is null) return null;
        return new PayPalAuthorizationInfo(authorization.Id, authorization.Status?.Value ?? "UNKNOWN",
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var auth = await Guarded<PaymentAuthorization, GetAuthorizedPaymentError>("get authorization",
            () => _client.Payments.GetAuthorizedPayment(authorizationId, null, null, ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);
        return new PayPalAuthorizationInfo(auth.Id ?? authorizationId, auth.Status?.Value ?? "UNKNOWN",
            ParseDate(auth.ExpirationTime));
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var auth = await Guarded<PaymentAuthorization, ReauthorizePaymentError>("reauthorize payment",
            () => _client.Payments.ReauthorizePayment(authorizationId, requestId, null, null,
                prefer: "return=representation", ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);
        return new PayPalAuthorizationInfo(auth.Id ?? authorizationId, auth.Status?.Value ?? "UNKNOWN",
            ParseDate(auth.ExpirationTime));
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var capture = await Guarded<CapturedPayment, CaptureAuthorizedPaymentError>("capture payment",
            () => _client.Payments.CaptureAuthorizedPayment(authorizationId, null, requestId, null, null,
                prefer: "return=representation", ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        if (capture.Id is null)
        {
            throw new PayPalException($"PayPal did not return a capture id for authorization {authorizationId}.");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var capturedAmount = ParseAmount(capture.Amount?.Value ?? breakdown?.GrossAmount.Value);
        return new PayPalCaptureResult(
            CaptureId: capture.Id,
            Status: capture.Status?.Value ?? "UNKNOWN",
            CapturedAmount: capturedAmount ?? 0m,
            CurrencyCode: capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount.CurrencyCode ?? string.Empty,
            PayPalFee: ParseAmount(breakdown?.PaypalFee?.Value),
            NetAmount: ParseAmount(breakdown?.NetAmount?.Value),
            TransactionTime: ParseDate(capture.CreateTime));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, requestId, ct: deadline.Token);
            _logger.LogInformation("PayPal void payment succeeded.");
        }
        catch (JsonException)
        {
            // Void returns 204 No Content; the SDK's typed return type has no body to deserialize and
            // throws. A genuine failure arrives as SdkException<VoidPaymentError>, not JsonException,
            // so an empty-body result here IS the successful void.
            _logger.LogInformation("PayPal void payment succeeded (204 No Content).");
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateTyped("void payment", ex.Error, e => e.TryGetError(out var er) ? er : null, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("void payment", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex, "PayPal void payment could not reach the provider.");
            throw new PayPalException("PayPal was unreachable or timed out during void payment.", inner: ex);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string requestId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } };

        var refund = await Guarded<Refund, RefundCapturedPaymentError>("refund payment",
            () => _client.Payments.RefundCapturedPayment(captureId, null, requestId, null, body,
                prefer: "return=representation", ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        if (refund.Id is null)
        {
            throw new PayPalException($"PayPal did not return a refund id for capture {captureId}.");
        }

        return new PayPalRefundResult(refund.Id, refund.Status?.Value ?? "UNKNOWN", ParseAmount(refund.Amount?.Value));
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var merchantCustomerId = SanitizeCustomerId(request.BuyerId);

        // PayPal's canonical vault-a-card flow: tokenize the raw card as a setup token, then exchange
        // it for a durable payment (vault) token. The setup token carries the customer association.
        var setupBody = new SetupTokenRequest
        {
            Customer = merchantCustomerId is null ? null : new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = BuildAddress(request.Card)
                }
            }
        };

        var setup = await Guarded<SetupTokenResponse, CreateSetupTokenError>("create setup token",
            () => _client.Vault.CreateSetupToken(null, setupBody, ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        if (setup.Id is null)
        {
            throw new PayPalException("PayPal did not return a setup token id when saving the card.");
        }

        var tokenBody = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest { Id = setup.Id, Type = VaultTokenRequestType.SetupToken }
            }
        };

        var token = await Guarded<PaymentTokenResponse, CreatePaymentTokenError>("vault card",
            () => _client.Vault.CreatePaymentToken(null, tokenBody, ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);

        if (token.Id is null)
        {
            throw new PayPalException("PayPal did not return a vault token id when saving the card.");
        }

        var cardEntity = token.PaymentSource?.Card;
        return new PayPalVaultResult(
            VaultId: token.Id,
            CustomerId: token.Customer?.Id,
            Brand: cardEntity?.Brand?.Value,
            Last4: cardEntity?.LastDigits,
            Expiry: cardEntity?.Expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        await Guarded<DeletePaymentTokenError>("delete vault token",
            () => _client.Vault.DeletePaymentToken(vaultId, ct: deadline.Token),
            e => e.TryGetError(out var er) ? er : null);
    }

    public async Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);

        var transactions = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        var totalItems = 0;
        var truncated = false;

        do
        {
            var current = page;
            var response = await GuardedRaw("search transactions",
                () => _client.TransactionSearch.SearchTransactions(startDate, endDate,
                    null, null, null, null, null, null, null, null,
                    fields: "transaction_info", balanceAffectingRecordsOnly: "Y", pageSize: 100, page: current,
                    ct: deadline.Token));

            totalPages = response.TotalPages ?? 1;
            totalItems = response.TotalItems ?? transactions.Count;

            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                transactions.Add(new PayPalTransaction(
                    TransactionId: info.TransactionId,
                    InvoiceId: info.InvoiceId,
                    CustomField: info.CustomField,
                    Amount: ParseAmount(info.TransactionAmount?.Value),
                    CurrencyCode: info.TransactionAmount?.CurrencyCode,
                    FeeAmount: ParseAmount(info.FeeAmount?.Value),
                    InitiationTime: ParseDate(info.TransactionInitiationDate),
                    Status: info.TransactionStatus));
            }

            if (page >= MaxReconciliationPages && page < totalPages)
            {
                truncated = true;
                _logger.LogWarning("Reconciliation search truncated at {Page} of {TotalPages} pages.", page, totalPages);
                break;
            }

            page++;
        }
        while (page <= totalPages);

        return new PayPalTransactionSearchResult(transactions, page - 1 > 0 ? page - 1 : 1, totalItems, truncated);
    }

    // ---------------- helpers ----------------

    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static CardRequest BuildCard(PayPalAuthorizeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new CardRequest { VaultId = request.VaultId };
        }

        var card = request.Card
            ?? throw new PayPalException("No payment method supplied: provide card details or a saved-card id.");

        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.BillingCountryCode)) return null;
        return new Address
        {
            AddressLine1 = card.BillingLine1,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode!
        };
    }

    private static string? SanitizeCustomerId(string buyerId)
    {
        // The spec regex permits [0-9a-zA-Z-_.^*$@#], but PayPal's vault service 500s on some of
        // those (e.g. '@', '.') — restrict to a safe alphanumeric+dash/underscore subset. Stable
        // per buyer, which is all we need to group a shopper's tokens.
        var cleaned = new string(buyerId.Where(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (cleaned.Length == 0) return null;
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }

    private async Task<T> Guarded<T, TError>(string op, Func<Task<T>> call, Func<TError, Error?> extractTyped)
        where TError : ApiError
    {
        try
        {
            var result = await call();
            _logger.LogInformation("PayPal {Operation} succeeded.", op);
            return result;
        }
        catch (SdkException<TError> ex)
        {
            throw TranslateTyped(op, ex.Error, extractTyped, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(op, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", op);
            throw new PayPalException($"PayPal returned a response that could not be processed during {op}.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", op);
            throw new PayPalException($"PayPal was unreachable or timed out during {op}.", inner: ex);
        }
    }

    private async Task Guarded<TError>(string op, Func<Task> call, Func<TError, Error?> extractTyped)
        where TError : ApiError
    {
        try
        {
            await call();
            _logger.LogInformation("PayPal {Operation} succeeded.", op);
        }
        catch (SdkException<TError> ex)
        {
            throw TranslateTyped(op, ex.Error, extractTyped, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(op, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", op);
            throw new PayPalException($"PayPal returned a response that could not be processed during {op}.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", op);
            throw new PayPalException($"PayPal was unreachable or timed out during {op}.", inner: ex);
        }
    }

    private async Task<T> GuardedRaw<T>(string op, Func<Task<T>> call)
    {
        try
        {
            var result = await call();
            _logger.LogInformation("PayPal {Operation} succeeded.", op);
            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(op, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", op);
            throw new PayPalException($"PayPal returned a response that could not be processed during {op}.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", op);
            throw new PayPalException($"PayPal was unreachable or timed out during {op}.", inner: ex);
        }
    }

    private PayPalException TranslateTyped<TError>(string op, TError error, Func<TError, Error?> extractTyped, Exception inner)
        where TError : ApiError
    {
        var typed = extractTyped(error);
        if (typed is not null)
        {
            // Surface the fine-grained issue code(s) — that is what an operator acts on, and what
            // distinguishes e.g. an expired authorization from a plain decline.
            var issues = typed.Details is { Count: > 0 }
                ? string.Join("; ", typed.Details.Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}"))
                : null;
            var detail = issues is null ? typed.Message : $"{typed.Message} ({issues})";
            _logger.LogWarning("PayPal {Operation} failed: {Name} ({DebugId}) {Detail}", op, typed.Name, typed.DebugId, detail);
            return new PayPalException(
                $"PayPal rejected the {op} request: {detail}",
                debugId: typed.DebugId, providerErrorName: typed.Name, inner: inner);
        }

        if (error.TryGetRawError(out var raw))
        {
            return FromRaw(op, raw, inner);
        }

        _logger.LogWarning(inner, "PayPal {Operation} failed with an unrecognised error shape.", op);
        return new PayPalException($"PayPal rejected the {op} request.", inner: inner);
    }

    private PayPalException FromRaw(string op, RawError raw, Exception inner)
    {
        _logger.LogWarning("PayPal {Operation} failed: HTTP {Status}", op, (int)raw.StatusCode);
        return new PayPalException($"PayPal returned HTTP {(int)raw.StatusCode} during {op}.",
            statusCode: raw.StatusCode, inner: inner);
    }
}

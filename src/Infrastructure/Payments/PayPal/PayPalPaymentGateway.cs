using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;
using PP = PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// The only place eShop talks to the PayPal SDK. Every call runs under the request's shared
/// <see cref="PayPalRequestBudget"/>, and every SDK failure leaves here as a <see cref="PaymentGatewayException"/>.
/// Card data passes through in memory only; nothing here logs a request body.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    // Every write asks for the full resource so authorization/capture/refund details come back in one response.
    private const string ReturnRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalRequestBudget _budget;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, PayPalRequestBudget budget, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _budget = budget;
        _logger = logger;
    }

    // ───────────────────────────── Orders ─────────────────────────────

    public Task<GatewayOrder> CreateOrderAsync(CreateGatewayOrder request, CancellationToken cancellationToken) =>
        RunAsync("CreateOrder", async ct =>
        {
            var order = await _client.Orders.CreateOrder(new CreateOrderRequest
            {
                PayPalRequestId = request.RequestId,
                Prefer = ReturnRepresentation,
                Body = new PP.OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits =
                    [
                        new PP.PurchaseUnitRequest
                        {
                            Amount = new PP.AmountWithBreakdown
                            {
                                CurrencyCode = request.Currency,
                                Value = MoneyFormat.Format(request.Amount, request.Currency)
                            },
                            CustomId = request.CustomId,
                            InvoiceId = request.InvoiceId,
                            Description = request.Description
                        }
                    ]
                }
            }, cancellationToken: ct);

            return new GatewayOrder(order.Id ?? throw MissingField("CreateOrder", "id"), order.Status?.Value);
        }, ex => ex is ApiException<CreateOrderError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayOrderAuthorization> AuthorizeOrderAsync(string gatewayOrderId, PaymentSourceInput source, string requestId, CancellationToken cancellationToken) =>
        RunAsync("AuthorizeOrder", async ct =>
        {
            var response = await _client.Orders.AuthorizeOrder(new AuthorizeOrderRequest
            {
                Id = gatewayOrderId,
                PayPalRequestId = requestId,
                Prefer = ReturnRepresentation,
                Body = new PP.OrderAuthorizeRequest
                {
                    PaymentSource = new PP.OrderAuthorizeRequestPaymentSource { Card = ToCardRequest(source) }
                }
            }, cancellationToken: ct);

            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.LastOrDefault();
            return new GatewayOrderAuthorization(
                response.Id ?? gatewayOrderId,
                response.Status?.Value,
                authorization is null ? null : ToAuthorization(authorization.Id, authorization.Status, authorization.Amount, authorization.CreateTime, authorization.ExpirationTime),
                response.PaymentSource?.Card?.Brand?.Value,
                response.PaymentSource?.Card?.LastDigits);
        }, ex => ex is ApiException<AuthorizeOrderError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayOrderAuthorization> GetOrderAsync(string gatewayOrderId, CancellationToken cancellationToken) =>
        RunAsync("GetOrder", async ct =>
        {
            var order = await _client.Orders.GetOrder(new GetOrderRequest { Id = gatewayOrderId }, cancellationToken: ct);
            var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.LastOrDefault();
            return new GatewayOrderAuthorization(
                order.Id ?? gatewayOrderId,
                order.Status?.Value,
                authorization is null ? null : ToAuthorization(authorization.Id, authorization.Status, authorization.Amount, authorization.CreateTime, authorization.ExpirationTime),
                order.PaymentSource?.Card?.Brand?.Value,
                order.PaymentSource?.Card?.LastDigits);
        }, ex => ex is ApiException<GetOrderError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    // ───────────────────────────── Payments ─────────────────────────────

    public Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        RunAsync("GetAuthorizedPayment", async ct =>
        {
            var auth = await _client.Payments.GetAuthorizedPayment(new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
            return ToAuthorization(auth.Id ?? authorizationId, auth.Status, auth.Amount, auth.CreateTime, auth.ExpirationTime)!;
        }, ex => ex is ApiException<GetAuthorizedPaymentError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken) =>
        RunAsync("ReauthorizePayment", async ct =>
        {
            var auth = await _client.Payments.ReauthorizePayment(new ReauthorizePaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = requestId,
                Prefer = ReturnRepresentation,
                Body = new PP.ReauthorizeRequest { Amount = ToMoney(amount, currency) }
            }, cancellationToken: ct);
            return ToAuthorization(auth.Id ?? throw MissingField("ReauthorizePayment", "id"), auth.Status, auth.Amount, auth.CreateTime, auth.ExpirationTime)!;
        }, ex => ex is ApiException<ReauthorizePaymentError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string requestId, CancellationToken cancellationToken) =>
        RunAsync("CaptureAuthorizedPayment", async ct =>
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(new CaptureAuthorizedPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = requestId,
                Prefer = ReturnRepresentation,
                Body = new PP.CaptureRequest
                {
                    Amount = ToMoney(amount, currency),
                    FinalCapture = true,
                    InvoiceId = invoiceId
                }
            }, cancellationToken: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            return new GatewayCapture(
                capture.Id ?? throw MissingField("CaptureAuthorizedPayment", "id"),
                capture.Status?.Value,
                MoneyFormat.Parse(capture.Amount?.Value) ?? MoneyFormat.Parse(breakdown?.GrossAmount.Value) ?? amount,
                capture.Amount?.CurrencyCode ?? currency,
                MoneyFormat.Parse(breakdown?.PaypalFee?.Value),
                MoneyFormat.Parse(breakdown?.NetAmount?.Value));
        }, ex => ex is ApiException<CaptureAuthorizedPaymentError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayAuthorization> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
        RunAsync("VoidPayment", async ct =>
        {
            var auth = await _client.Payments.VoidPayment(new VoidPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = requestId,
                Prefer = ReturnRepresentation
            }, cancellationToken: ct);
            // A 2xx means the void was accepted; a body without a status still means VOIDED.
            return new GatewayAuthorization(auth?.Id ?? authorizationId, auth?.Status?.Value ?? AuthorizationStatus.Voided.Value,
                MoneyFormat.Parse(auth?.Amount?.Value), auth?.Amount?.CurrencyCode, ParseDate(auth?.CreateTime), ParseDate(auth?.ExpirationTime));
        }, ex => ex is ApiException<VoidPaymentError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task<GatewayRefund> RefundAsync(string captureId, decimal amount, string currency, string customId, string requestId, CancellationToken cancellationToken) =>
        RunAsync("RefundCapturedPayment", async ct =>
        {
            var refund = await _client.Payments.RefundCapturedPayment(new RefundCapturedPaymentRequest
            {
                CaptureId = captureId,
                PayPalRequestId = requestId,
                Prefer = ReturnRepresentation,
                // Always explicit: an empty body would mean "refund everything that is left".
                Body = new PP.RefundRequest { Amount = ToMoney(amount, currency), CustomId = customId }
            }, cancellationToken: ct);
            return new GatewayRefund(refund.Id ?? throw MissingField("RefundCapturedPayment", "id"), refund.Status?.Value, MoneyFormat.Parse(refund.Amount?.Value));
        }, ex => ex is ApiException<RefundCapturedPaymentError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    // ───────────────────────────── Vault ─────────────────────────────

    public Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken) =>
        RunAsync("CreatePaymentToken", async ct =>
        {
            var token = await _client.Vault.CreatePaymentToken(new CreatePaymentTokenRequest
            {
                PayPalRequestId = requestId,
                Body = new PP.PaymentTokenRequest
                {
                    Customer = customerId is null ? null : new PP.Customer { Id = customerId },
                    PaymentSource = new PP.PaymentTokenRequestPaymentSource
                    {
                        Card = new PP.PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToAddress(card.BillingAddress)
                        }
                    }
                }
            }, cancellationToken: ct);

            var vaulted = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                token.Id ?? throw MissingField("CreatePaymentToken", "id"),
                token.Customer?.Id,
                vaulted?.Brand?.Value,
                vaulted?.LastDigits,
                vaulted?.Expiry,
                vaulted?.Name);
        }, ex => ex is ApiException<CreatePaymentTokenError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken) =>
        RunAsync("DeletePaymentToken", async ct =>
        {
            await _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = vaultId }, cancellationToken: ct);
            return true;
        }, ex => ex is ApiException<DeletePaymentTokenError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

    public async Task<bool> VaultedCardExistsAsync(string vaultId, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync("GetPaymentToken", async ct =>
            {
                await _client.Vault.GetPaymentToken(new GetPaymentTokenRequest { Id = vaultId }, cancellationToken: ct);
                return true;
            }, ex => ex is ApiException<GetPaymentTokenError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);
            return true;
        }
        catch (PaymentGatewayException ex) when (ex.Failure == PaymentGatewayFailure.NotFound)
        {
            return false;
        }
    }

    public async Task<(IReadOnlyList<string> VaultIds, bool Complete)> ListVaultedCardsAsync(string customerId, CancellationToken cancellationToken)
    {
        const int maxPages = 10;
        var ids = new List<string>();
        for (var page = 1; page <= maxPages; page++)
        {
            var current = page;
            var response = await RunAsync("ListCustomerPaymentTokens", ct =>
                _client.Vault.ListCustomerPaymentTokens(new ListCustomerPaymentTokensRequest
                {
                    CustomerId = customerId,
                    Page = current,
                    PageSize = 20,
                    TotalRequired = true
                }, cancellationToken: ct),
                ex => ex is ApiException<ListCustomerPaymentTokensError> e && e.Error.TryGetError(out var body) ? body : null, cancellationToken);

            var tokens = response.PaymentTokens ?? [];
            ids.AddRange(tokens.Select(t => t.Id).Where(id => id is not null)!);
            if (tokens.Count == 0 || page >= (response.TotalPages ?? 1))
                return (ids, true);
        }
        return (ids, false);
    }

    // ───────────────────────────── Reporting ─────────────────────────────

    public Task<GatewayTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken cancellationToken) =>
        RunAsync("SearchTransactions", async ct =>
        {
            var response = await _client.TransactionSearch.SearchTransactions(new SearchTransactionsRequest
            {
                StartDate = ToRfc3339(from),
                EndDate = ToRfc3339(to),
                Page = page,
                PageSize = pageSize
            }, cancellationToken: ct);

            var items = (response.TransactionDetails ?? [])
                .Select(d => d.TransactionInfo)
                .Where(i => i?.TransactionId is not null)
                .Select(i => new GatewayTransaction(
                    i!.TransactionId!,
                    i.TransactionEventCode,
                    i.TransactionStatus,
                    ParseDate(i.TransactionInitiationDate),
                    MoneyFormat.Parse(i.TransactionAmount?.Value),
                    i.TransactionAmount?.CurrencyCode,
                    MoneyFormat.Parse(i.FeeAmount?.Value),
                    i.InvoiceId,
                    i.CustomField,
                    i.PaypalReferenceId))
                .ToList();
            return new GatewayTransactionPage(items, response.Page ?? page, response.TotalPages ?? page);
        }, _ => null, cancellationToken);

    // ───────────────────────────── error boundary ─────────────────────────────

    /// <summary>
    /// Runs one SDK call under the request budget and translates every failure. <paramref name="typedBody"/> reads
    /// the operation's own typed error (its <c>TryGetError</c> accessor) inside the concrete exception type.
    /// </summary>
    private async Task<T> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> call,
        Func<ApiException, PP.Error?> typedBody, CancellationToken cancellationToken)
    {
        if (_budget.IsExhausted)
            throw BudgetExhausted(operation, null);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _budget.Token);
        try
        {
            return await call(linked.Token);
        }
        catch (ResponseDeserializationException ex)
        {
            // The server answered but the body did not match. 2xx: outcome unknown. Otherwise: rejected, detail lost.
            var status = (int)ex.StatusCode;
            _logger.LogWarning("PayPal {Operation} returned HTTP {Status} with an unreadable body.", operation, status);
            throw status is >= 200 and < 300
                ? new PaymentGatewayException(PaymentGatewayFailure.ProviderError, "PayPal returned a response that could not be processed.", status, inner: ex)
                : Translate(operation, status, null, ex);
        }
        catch (ApiException<RawError> ex)
        {
            // Case B operations (transaction search): no typed body; PayPal's JSON error shape is still worth reading.
            throw Translate(operation, (int)ex.StatusCode, TryReadError(ex.Error), ex);
        }
        catch (ApiException ex)
        {
            throw Translate(operation, (int)ex.StatusCode, typedBody(ex), ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning("PayPal {Operation} did not respond within {Timeout}.", operation, ex.Timeout);
            throw new PaymentGatewayException(PaymentGatewayFailure.Timeout, "PayPal did not respond in time.", inner: ex,
                budgetExhausted: _budget.IsExhausted);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning("PayPal {Operation} could not be completed: connection failure.", operation);
            throw new PaymentGatewayException(PaymentGatewayFailure.Unreachable, "PayPal could not be reached.", inner: ex,
                budgetExhausted: _budget.IsExhausted);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex.InnerException, "PayPal {Operation}: the merchant credentials could not be applied (token request failed).", operation);
            throw new PaymentGatewayException(PaymentGatewayFailure.MerchantConfiguration,
                "PayPal rejected the merchant credentials; check PayPal:ClientId / PayPal:ClientSecret.", inner: ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && _budget.IsExhausted)
        {
            // Our own deadline fired (the caller did not cancel): PayPal did not answer within the request budget.
            throw BudgetExhausted(operation, ex);
        }
    }

    private PaymentGatewayException BudgetExhausted(string operation, Exception? inner)
    {
        _logger.LogWarning("PayPal {Operation} abandoned: the request's PayPal time budget is exhausted.", operation);
        return new PaymentGatewayException(PaymentGatewayFailure.Timeout, "PayPal did not respond in time.", inner: inner, budgetExhausted: true);
    }

    private PaymentGatewayException Translate(string operation, int status, PP.Error? error, Exception inner)
    {
        var detail = error?.Details?.FirstOrDefault();
        var issue = detail?.Issue;
        var message = detail?.Description ?? error?.Message ?? $"PayPal answered HTTP {status}.";
        var failure = status switch
        {
            401 or 403 => PaymentGatewayFailure.MerchantConfiguration,
            404 => PaymentGatewayFailure.NotFound,
            >= 400 and < 500 and not 408 and not 429 => PaymentGatewayFailure.Rejected,
            _ => PaymentGatewayFailure.ProviderError
        };

        _logger.LogWarning("PayPal {Operation} failed: HTTP {Status} {Name} {Issue} (debug id {DebugId})",
            operation, status, error?.Name ?? "-", issue ?? "-", error?.DebugId ?? "-");

        if (failure == PaymentGatewayFailure.MerchantConfiguration)
            message = $"PayPal refused the merchant account's credentials or permissions for this operation ({error?.Name ?? $"HTTP {status}"}).";

        return new PaymentGatewayException(failure, message, status, error?.Name, issue, error?.DebugId, inner);
    }

    private static PP.Error? TryReadError(RawError raw)
    {
        try
        {
            return raw.ReadAsJson<PP.Error>();
        }
        catch (Exception)
        {
            return null; // not JSON (e.g. a gateway HTML page) — the status still classifies it
        }
    }

    // ───────────────────────────── mapping ─────────────────────────────

    private static PP.CardRequest ToCardRequest(PaymentSourceInput source)
    {
        if (source.VaultId is not null)
            return new PP.CardRequest { VaultId = source.VaultId };

        var card = source.Card!;
        return new PP.CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static PP.Address? ToAddress(CardBillingAddress? address) => address is null ? null : new PP.Address
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode.ToUpperInvariant()
    };

    private static PP.Money ToMoney(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = MoneyFormat.Format(amount, currency)
    };

    private static GatewayAuthorization? ToAuthorization(string? id, AuthorizationStatus? status, PP.Money? amount, string? createTime, string? expirationTime) =>
        id is null ? null : new GatewayAuthorization(id, status?.Value, MoneyFormat.Parse(amount?.Value), amount?.CurrencyCode,
            ParseDate(createTime), ParseDate(expirationTime));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static PaymentGatewayException MissingField(string operation, string field) =>
        new(PaymentGatewayFailure.ProviderError, $"PayPal {operation} response did not include '{field}'.", (int)HttpStatusCode.OK);
}

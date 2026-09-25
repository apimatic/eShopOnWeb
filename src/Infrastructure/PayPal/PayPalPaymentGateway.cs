using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The concrete PayPal integration. Every PayPal SDK call is made here and translated to the domain's
/// plain records / <see cref="PayPalGatewayException"/>; no SDK type escapes. Card details are sent to
/// PayPal but never persisted or logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // A capture can be walked over at most this many pages per reporting window (safety backstop).
    private const int MaxPagesPerWindow = 1000;
    private const int TransactionSearchPageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    // ---- Authorize (create order + hold funds) -----------------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken)
    {
        var card = BuildCardRequest(request);

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    },
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = request.Description
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        var order = await InvokeAsync<Order, CreateOrderError>(
            "create-order",
            ct => _client.Orders.CreateOrder(new CreateOrderRequest
            {
                Body = body,
                PayPalRequestId = request.RequestId,
                Prefer = "return=representation"
            }, cancellationToken: ct),
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal rejected the order."),
            idempotentResend: true,
            cancellationToken);

        var payPalOrderId = order.Id ?? throw Unexpected("create-order", "PayPal returned no order id.");
        var (authorization, cardResp) = ReadAuthorization(order.PurchaseUnits, order.PaymentSource);

        // Authorization not present in the create response: authorize explicitly if the order was approved,
        // or surface a browser-approval challenge.
        if (authorization is null)
        {
            if (order.Status == OrderStatus.Approved)
            {
                var authResponse = await InvokeAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
                    "authorize-order",
                    ct => _client.Orders.AuthorizeOrder(new AuthorizeOrderRequest
                    {
                        Id = payPalOrderId,
                        PayPalRequestId = $"{request.RequestId}-auth",
                        Prefer = "return=representation"
                    }, cancellationToken: ct),
                    e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not authorize the order."),
                    idempotentResend: true,
                    cancellationToken);

                (authorization, _) = ReadAuthorization(authResponse.PurchaseUnits, null);
            }
            else if (RequiresApproval(order.Status, order.Links))
            {
                return new PayPalAuthorizationResult
                {
                    PayPalOrderId = payPalOrderId,
                    RequiresApproval = true,
                    ApprovalMessage = "PayPal requires the shopper to approve this card payment in a browser " +
                                      "(challenge/3-D Secure); this direct-card API does not support that round-trip."
                };
            }
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
            throw Unexpected("authorize-order", $"PayPal produced no authorization (order status {order.Status?.Value}).");

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id,
            Status = authorization.Status?.Value,
            ExpiresAt = ParseDate(authorization.ExpirationTime),
            CardBrand = cardResp?.Brand?.Value,
            CardLastDigits = cardResp?.LastDigits
        };
    }

    // ---- Capture -----------------------------------------------------------------------------

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var captured = await InvokeAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            "capture-authorization",
            ct => _client.Payments.CaptureAuthorizedPayment(new CaptureAuthorizedPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = requestId,
                Prefer = "return=representation",
                Body = new CaptureRequest { FinalCapture = true }
            }, cancellationToken: ct),
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not capture the payment."),
            idempotentResend: true,
            cancellationToken);

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = ParseDecimal(captured.Amount?.Value)
            ?? ParseDecimal(breakdown?.GrossAmount?.Value)
            ?? amount;

        return new PayPalCaptureResult
        {
            CaptureId = captured.Id ?? throw Unexpected("capture-authorization", "PayPal returned no capture id."),
            Status = captured.Status?.Value,
            CapturedAmount = capturedAmount,
            PayPalFee = ParseDecimal(breakdown?.PaypalFee?.Value),
            NetAmount = ParseDecimal(breakdown?.NetAmount?.Value),
            Currency = captured.Amount?.CurrencyCode ?? currency
        };
    }

    // ---- Reauthorize -------------------------------------------------------------------------

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var auth = await InvokeAsync<PaymentAuthorization, ReauthorizePaymentError>(
            "reauthorize",
            ct => _client.Payments.ReauthorizePayment(new ReauthorizePaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = requestId,
                Prefer = "return=representation",
                Body = new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            }, cancellationToken: ct),
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not reauthorize the payment."),
            idempotentResend: true,
            cancellationToken);

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = auth.Id,
            Status = auth.Status?.Value,
            ExpiresAt = ParseDate(auth.ExpirationTime)
        };
    }

    // ---- Void --------------------------------------------------------------------------------

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await InvokeAsync<PaymentAuthorization, VoidPaymentError>(
                "void-authorization",
                ct => _client.Payments.VoidPayment(new VoidPaymentRequest { AuthorizationId = authorizationId },
                    cancellationToken: ct),
                e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not void the authorization."),
                idempotentResend: true,
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode is 204 or 200 or 404 or 409 or 422)
        {
            // 204/200 = void succeeded (PayPal returns an empty body for a void, which the typed
            // PaymentAuthorization cannot deserialize); 404/409/422 = already voided/captured/expired.
            // Either way the hold is no longer standing, which is the outcome cancel wants.
            _logger.LogInformation("Void of authorization {AuthorizationId} settled (status {Status}); hold released.",
                authorizationId, ex.StatusCode);
        }
    }

    // ---- Refund ------------------------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        RefundRequest? refundBody = amount is decimal value
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(value) } }
            : null;

        var refund = await InvokeAsync<Refund, RefundCapturedPaymentError>(
            "refund-capture",
            ct => _client.Payments.RefundCapturedPayment(new RefundCapturedPaymentRequest
            {
                CaptureId = captureId,
                PayPalRequestId = requestId,
                Prefer = "return=representation",
                Body = refundBody
            }, cancellationToken: ct),
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not refund the capture."),
            idempotentResend: true,
            cancellationToken);

        return new PayPalRefundResult
        {
            RefundId = refund.Id ?? throw Unexpected("refund-capture", "PayPal returned no refund id."),
            Status = refund.Status?.Value,
            Amount = ParseDecimal(refund.Amount?.Value) ?? amount ?? 0m,
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    // ---- Vault card --------------------------------------------------------------------------

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken)
    {
        Customer? customer = null;
        if (!string.IsNullOrEmpty(request.PayPalCustomerId))
            customer = new Customer { Id = request.PayPalCustomerId };
        else if (!string.IsNullOrEmpty(request.MerchantCustomerId))
            customer = new Customer { MerchantCustomerId = SanitizeMerchantCustomerId(request.MerchantCustomerId) };

        var body = new PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardHolderName,
                    BillingAddress = BuildAddress(request.Card)
                }
            }
        };

        var token = await InvokeAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            "vault-card",
            ct => _client.Vault.CreatePaymentToken(new CreatePaymentTokenRequest
            {
                Body = body,
                PayPalRequestId = request.RequestId
            }, cancellationToken: ct),
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not vault the card."),
            idempotentResend: false,
            cancellationToken);

        var cardEntity = token.PaymentSource?.Card;
        return new PayPalVaultResult
        {
            VaultId = token.Id ?? throw Unexpected("vault-card", "PayPal returned no vault id."),
            PayPalCustomerId = token.Customer?.Id ?? request.PayPalCustomerId,
            Brand = cardEntity?.Brand?.Value,
            LastDigits = cardEntity?.LastDigits,
            Expiry = cardEntity?.Expiry,
            CardHolderName = cardEntity?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        await InvokeAsync<bool, DeletePaymentTokenError>(
            "delete-vault-card",
            async ct =>
            {
                await _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = vaultId }, cancellationToken: ct);
                return true;
            },
            e => e.TryGetError(out var err) && err is not null ? Describe(err) : (null, null, "PayPal could not delete the vaulted card."),
            idempotentResend: true,
            cancellationToken);
    }

    // ---- Transaction search (reconciliation) -------------------------------------------------

    public async Task<PayPalTransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var collected = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int pagesRetrieved = 0;
        int windows = 0;

        // PayPal's reporting API caps a query at 31 days — split the requested range into ≤31-day windows
        // and walk every page of each, so the report covers the whole range, not just the first page.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd >= to) windowEnd = to;
            windows++;

            int page = 1;
            int totalPages;
            do
            {
                var response = await SearchPageAsync(windowStart, windowEnd, page, cancellationToken);
                pagesRetrieved++;
                totalPages = response.TotalPages ?? 1;

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var mapped = MapTransaction(detail);
                        var key = $"{mapped.TransactionId}|{mapped.EventCode}";
                        if (seen.Add(key))
                            collected.Add(mapped);
                    }
                }
                page++;
            }
            while (page <= totalPages && page <= MaxPagesPerWindow);

            if (windowEnd >= to) break;
            windowStart = windowEnd;
        }

        return new PayPalTransactionSearchResult
        {
            Transactions = collected,
            PagesRetrieved = pagesRetrieved,
            WindowsQueried = windows,
            Truncated = false
        };
    }

    private Task<SearchResponse> SearchPageAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken) =>
        InvokeAsync<SearchResponse, RawError>(
            "search-transactions",
            ct => _client.TransactionSearch.SearchTransactions(new SearchTransactionsRequest
            {
                StartDate = FormatDate(from),
                EndDate = FormatDate(to),
                Fields = "transaction_info",
                BalanceAffectingRecordsOnly = "N",
                PageSize = TransactionSearchPageSize,
                Page = page
            }, cancellationToken: ct),
            raw => (null, null, $"PayPal transaction search failed (HTTP {(int)raw.StatusCode})."),
            idempotentResend: true,
            cancellationToken);

    private static PayPalTransaction MapTransaction(TransactionDetails detail)
    {
        var info = detail.TransactionInfo;
        return new PayPalTransaction
        {
            TransactionId = info?.TransactionId,
            InvoiceId = info?.InvoiceId,
            CustomField = info?.CustomField,
            Amount = ParseDecimal(info?.TransactionAmount?.Value),
            Currency = info?.TransactionAmount?.CurrencyCode,
            FeeAmount = ParseDecimal(info?.FeeAmount?.Value),
            Status = info?.TransactionStatus,
            EventCode = info?.TransactionEventCode,
            InitiationDate = ParseDate(info?.TransactionInitiationDate)
        };
    }

    // ---- Building blocks ---------------------------------------------------------------------

    private static CardRequest BuildCardRequest(PayPalAuthorizeRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
            return new CardRequest { VaultId = request.VaultId };

        if (request.Card is null)
            throw new PayPalGatewayException("No card or saved card was supplied for the payment.", 400, "MISSING_INSTRUMENT");

        var card = request.Card;
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardHolderName,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrEmpty(card.BillingCountryCode))
            return null;

        return new Address
        {
            CountryCode = card.BillingCountryCode!,
            AddressLine1 = card.BillingStreet,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode
        };
    }

    private static (AuthorizationWithAdditionalData? authorization, CardResponse? card) ReadAuthorization(
        IReadOnlyList<PurchaseUnit>? purchaseUnits, PaymentSourceResponse? paymentSource)
    {
        AuthorizationWithAdditionalData? authorization = null;
        if (purchaseUnits is not null)
        {
            foreach (var unit in purchaseUnits)
            {
                var candidate = unit.Payments?.Authorizations?.FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
                if (candidate is not null)
                {
                    authorization = candidate;
                    break;
                }
            }
        }
        return (authorization, paymentSource?.Card);
    }

    private static bool RequiresApproval(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired)
            return true;
        return links?.Any(l => l.Rel is "approve" or "payer-action") == true;
    }

    private static string SanitizeMerchantCustomerId(string value)
    {
        // Allowed by PayPal: [0-9a-zA-Z-_.^*$@#], max 64.
        var cleaned = new string(value.Where(c =>
            char.IsLetterOrDigit(c) || "-_.^*$@#".IndexOf(c) >= 0).ToArray());
        if (cleaned.Length == 0) cleaned = "shopper";
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto) ? dto : null;

    private static (string? issue, string? debugId, string message) Describe(Error error)
    {
        var issue = error.Details is { Count: > 0 } details ? details[0].Issue : error.Name;
        return (issue, error.DebugId, error.Message);
    }

    private PayPalGatewayException Unexpected(string op, string message)
    {
        _logger.LogWarning("PayPal {Operation}: {Message}", op, message);
        return new PayPalGatewayException(message, 502, "UNEXPECTED_RESPONSE");
    }

    /// <summary>
    /// Runs one SDK call, bounding the whole call with a cancellation deadline, translating SDK exceptions to
    /// <see cref="PayPalGatewayException"/>. For an idempotent write (carrying a PayPal-Request-Id) a single
    /// transport-failure resend settles the unknown outcome — PayPal returns the same result under the key.
    /// </summary>
    private async Task<T> InvokeAsync<T, TError>(
        string op,
        Func<CancellationToken, Task<T>> call,
        Func<TError, (string? issue, string? debugId, string message)> extract,
        bool idempotentResend,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.CallTimeoutSeconds));

            try
            {
                return await call(cts.Token);
            }
            catch (ApiException<TError> ex)
            {
                var (issue, debugId, message) = extract(ex.Error);
                _logger.LogWarning("PayPal {Operation} error: status={Status} issue={Issue} debugId={DebugId}",
                    op, (int)ex.StatusCode, issue, debugId);
                throw new PayPalGatewayException(message, (int)ex.StatusCode, issue, debugId, outcomeUnknown: false, ex);
            }
            catch (ResponseDeserializationException ex)
            {
                _logger.LogWarning(ex, "PayPal {Operation}: response could not be processed (status {Status}).", op, ex.StatusCode);
                throw new PayPalGatewayException("PayPal returned a response that could not be processed.",
                    (int)ex.StatusCode, null, null, outcomeUnknown: false, ex);
            }
            catch (AuthSchemeException ex)
            {
                _logger.LogWarning(ex, "PayPal {Operation}: credentials could not be applied.", op);
                throw new PayPalGatewayException("PayPal credentials could not be applied.", null, null, null, outcomeUnknown: false, ex);
            }
            catch (SdkException ex) when (ex is SdkConnectionException or SdkTimeoutException && idempotentResend && attempt == 1)
            {
                _logger.LogWarning(ex, "PayPal {Operation}: transport failure; resending once under the same request id.", op);
                // loop and resend with the same request (same PayPal-Request-Id) to settle the outcome
            }
            catch (SdkException ex) when (ex is SdkConnectionException or SdkTimeoutException)
            {
                _logger.LogWarning(ex, "PayPal {Operation}: transport failure; outcome unknown.", op);
                throw new PayPalGatewayException(
                    $"PayPal did not confirm the {op} outcome (transport failure). The operation may or may not have completed.",
                    null, null, null, outcomeUnknown: true, ex);
            }
            catch (SdkException ex)
            {
                _logger.LogWarning(ex, "PayPal {Operation}: SDK error.", op);
                throw new PayPalGatewayException($"PayPal call '{op}' failed.", null, null, null, outcomeUnknown: false, ex);
            }
        }
    }
}

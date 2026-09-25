using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The only place that talks to the PayPal SDK. Translates every SDK failure into
/// <see cref="PaymentGatewayException"/> (or a subtype) and never leaks SDK types across the boundary.
/// Every call is bounded by a whole-call deadline via a linked <see cref="CancellationToken"/>.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    private delegate bool ErrorAccessor<in TError>(TError error, out Error typed);

    // ---------------------------------------------------------------- Authorize

    public Task<GatewayAuthorization> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct) =>
        Guarded("Authorize", ct, async token =>
        {
            var orderRequest = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = command.CurrencyCode,
                            Value = FormatAmount(command.Amount)
                        },
                        InvoiceId = command.ReferenceId,
                        CustomId = command.ReferenceId,
                        Description = Truncate(command.Description, 127)
                    }
                },
                PaymentSource = BuildPaymentSource(command.Card, command.VaultId)
            };

            _logger.LogInformation($"CreateOrder invoice_id={command.ReferenceId} amount={FormatAmount(command.Amount)} {command.CurrencyCode} vaulted={(command.VaultId is not null)}");
            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(new CreateOrderRequest
                {
                    Body = orderRequest,
                    PayPalRequestId = command.IdempotencyKeyBase,
                    Prefer = "return=representation"
                }, cancellationToken: token);
            }
            catch (ApiException<CreateOrderError> ex)
            {
                throw Translate(ex, "CreateOrder", static (CreateOrderError e, out Error o) => e.TryGetError(out o));
            }

            var payPalOrderId = created.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id.");
            EnsureNoChallenge(created.Status, created.Links, created.PurchaseUnits);

            var auth = ExtractAuthorization(created.PurchaseUnits);
            if (auth is null)
            {
                OrderAuthorizeResponse authorized;
                try
                {
                    authorized = await _client.Orders.AuthorizeOrder(new AuthorizeOrderRequest
                    {
                        Id = payPalOrderId,
                        PayPalRequestId = command.IdempotencyKeyBase + "-auth",
                        Prefer = "return=representation"
                    }, cancellationToken: token);
                }
                catch (ApiException<AuthorizeOrderError> ex)
                {
                    throw Translate(ex, "AuthorizeOrder", static (AuthorizeOrderError e, out Error o) => e.TryGetError(out o));
                }

                EnsureNoChallenge(authorized.Status, authorized.Links, authorized.PurchaseUnits);
                auth = ExtractAuthorization(authorized.PurchaseUnits);
            }

            if (auth is null)
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
            }

            return new GatewayAuthorization(payPalOrderId, auth.Value.Id, auth.Value.Status, auth.Value.ExpiresAt, auth.Value.Amount, command.CurrencyCode);
        });

    public Task<GatewayAuthorization?> TryGetAuthorizationForOrderAsync(string payPalOrderId, CancellationToken ct) =>
        Guarded<GatewayAuthorization?>("GetOrder", ct, async token =>
        {
            Order order;
            try
            {
                order = await _client.Orders.GetOrder(new GetOrderRequest { Id = payPalOrderId }, cancellationToken: token);
            }
            catch (ApiException<GetOrderError> ex)
            {
                throw Translate(ex, "GetOrder", static (GetOrderError e, out Error o) => e.TryGetError(out o));
            }

            var auth = ExtractAuthorization(order.PurchaseUnits);
            return auth is null
                ? null
                : new GatewayAuthorization(payPalOrderId, auth.Value.Id, auth.Value.Status, auth.Value.ExpiresAt, auth.Value.Amount,
                    order.PurchaseUnits?.FirstOrDefault()?.Amount?.CurrencyCode ?? string.Empty);
        });

    public Task<GatewayAuthorizationState?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        Guarded<GatewayAuthorizationState?>("GetAuthorizedPayment", ct, async token =>
        {
            PaymentAuthorization auth;
            try
            {
                auth = await _client.Payments.GetAuthorizedPayment(new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId }, cancellationToken: token);
            }
            catch (ApiException<GetAuthorizedPaymentError> ex)
            {
                throw Translate(ex, "GetAuthorizedPayment", static (GetAuthorizedPaymentError e, out Error o) => e.TryGetError(out o));
            }

            return new GatewayAuthorizationState(auth.Id ?? authorizationId, auth.Status?.Value, ParseTime(auth.ExpirationTime));
        });

    // ---------------------------------------------------------------- Capture / reauth / void

    public Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct) =>
        Guarded("Capture", ct, async token =>
        {
            CapturedPayment captured;
            try
            {
                captured = await _client.Payments.CaptureAuthorizedPayment(new CaptureAuthorizedPaymentRequest
                {
                    AuthorizationId = authorizationId,
                    PayPalRequestId = idempotencyKey,
                    Prefer = "return=representation",
                    Body = new CaptureRequest { FinalCapture = true } // capture the full held amount
                }, cancellationToken: token);
            }
            catch (ApiException<CaptureAuthorizedPaymentError> ex)
            {
                throw Translate(ex, "CaptureAuthorizedPayment", static (CaptureAuthorizedPaymentError e, out Error o) => e.TryGetError(out o));
            }

            var breakdown = captured.SellerReceivableBreakdown;
            return new GatewayCapture(
                captured.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
                captured.Status?.Value,
                ParseMoney(captured.Amount),
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                captured.Amount?.CurrencyCode ?? string.Empty);
        });

    public Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct) =>
        Guarded("Reauthorize", ct, async token =>
        {
            PaymentAuthorization reauth;
            try
            {
                reauth = await _client.Payments.ReauthorizePayment(new ReauthorizePaymentRequest
                {
                    AuthorizationId = authorizationId,
                    PayPalRequestId = idempotencyKey,
                    Prefer = "return=representation",
                    Body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) } }
                }, cancellationToken: token);
            }
            catch (ApiException<ReauthorizePaymentError> ex)
            {
                // A hold that can no longer be renewed surfaces as a 422/404 here.
                throw Translate(ex, "ReauthorizePayment", static (ReauthorizePaymentError e, out Error o) => e.TryGetError(out o), renewableGuard: true);
            }

            return new GatewayAuthorization(
                string.Empty,
                reauth.Id ?? authorizationId,
                reauth.Status?.Value,
                ParseTime(reauth.ExpirationTime),
                ParseMoney(reauth.Amount),
                currencyCode);
        });

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct) =>
        Guarded<bool>("Void", ct, async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(new VoidPaymentRequest
                {
                    AuthorizationId = authorizationId,
                    PayPalRequestId = idempotencyKey
                }, cancellationToken: token);
            }
            catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
            {
                // A successful void returns 204 No Content, but the operation declares a PaymentAuthorization
                // body — so the empty body raises a deserialization error on what is actually a success.
                // A 2xx here means the void went through; treat it as success.
                _logger.LogInformation($"VoidPayment for {authorizationId} returned {(int)ex.StatusCode} with no body (expected for a void); treating as success.");
            }
            catch (ApiException<VoidPaymentError> ex)
            {
                throw Translate(ex, "VoidPayment", static (VoidPaymentError e, out Error o) => e.TryGetError(out o));
            }
            return true;
        });

    // ---------------------------------------------------------------- Refund

    public Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, string? note, CancellationToken ct) =>
        Guarded("Refund", ct, async token =>
        {
            RefundRequest? body = null;
            if (amount.HasValue || !string.IsNullOrWhiteSpace(note))
            {
                body = new RefundRequest
                {
                    Amount = amount.HasValue ? new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } : null,
                    NoteToPayer = string.IsNullOrWhiteSpace(note) ? null : Truncate(note!, 255)
                };
            }

            Refund refund;
            try
            {
                refund = await _client.Payments.RefundCapturedPayment(new RefundCapturedPaymentRequest
                {
                    CaptureId = captureId,
                    PayPalRequestId = idempotencyKey,
                    Prefer = "return=representation",
                    Body = body
                }, cancellationToken: token);
            }
            catch (ApiException<RefundCapturedPaymentError> ex)
            {
                throw Translate(ex, "RefundCapturedPayment", static (RefundCapturedPaymentError e, out Error o) => e.TryGetError(out o));
            }

            return new GatewayRefund(
                refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
                refund.Status?.Value,
                ParseMoney(refund.Amount),
                refund.Amount?.CurrencyCode ?? currencyCode);
        });

    // ---------------------------------------------------------------- Vault

    public Task<GatewayVaultedCard> VaultCardAsync(VaultCardCommand command, CancellationToken ct) =>
        Guarded("VaultCard", ct, async token =>
        {
            // 1) Create a setup token that holds the card.
            SetupTokenResponse setup;
            try
            {
                setup = await _client.Vault.CreateSetupToken(new CreateSetupTokenRequest
                {
                    PayPalRequestId = command.IdempotencyKey + "-setup",
                    Body = new SetupTokenRequest
                    {
                        Customer = string.IsNullOrWhiteSpace(command.CustomerId) ? null : new Customer { Id = command.CustomerId },
                        PaymentSource = new SetupTokenRequestPaymentSource
                        {
                            Card = new SetupTokenRequestCard
                            {
                                Number = command.Card.Number,
                                Expiry = command.Card.Expiry,
                                SecurityCode = command.Card.SecurityCode,
                                Name = command.Card.CardholderName
                            }
                        }
                    }
                }, cancellationToken: token);
            }
            catch (ApiException<CreateSetupTokenError> ex)
            {
                throw Translate(ex, "CreateSetupToken", static (CreateSetupTokenError e, out Error o) => e.TryGetError(out o));
            }

            var setupTokenId = setup.Id ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");

            // 2) Exchange the setup token for a durable payment (vault) token.
            PaymentTokenResponse tokenResponse;
            try
            {
                tokenResponse = await _client.Vault.CreatePaymentToken(new CreatePaymentTokenRequest
                {
                    PayPalRequestId = command.IdempotencyKey,
                    Body = new PaymentTokenRequest
                    {
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Token = new VaultTokenRequest
                            {
                                Id = setupTokenId,
                                Type = VaultTokenRequestType.SetupToken
                            }
                        }
                    }
                }, cancellationToken: token);
            }
            catch (ApiException<CreatePaymentTokenError> ex)
            {
                throw Translate(ex, "CreatePaymentToken", static (CreatePaymentTokenError e, out Error o) => e.TryGetError(out o));
            }

            var vaultId = tokenResponse.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
            var card = tokenResponse.PaymentSource?.Card;
            return new GatewayVaultedCard(
                vaultId,
                card?.Brand?.Value,
                card?.LastDigits,
                card?.Expiry,
                card?.Name,
                tokenResponse.Customer?.Id);
        });

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct) =>
        Guarded<bool>("DeleteVaultedCard", ct, async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = vaultId }, cancellationToken: token);
            }
            catch (ApiException<DeletePaymentTokenError> ex) when ((int)ex.StatusCode == 404)
            {
                // Already gone at PayPal — treat delete as idempotent.
                _logger.LogWarning($"Vault token {vaultId} was already absent at PayPal on delete.");
            }
            catch (ApiException<DeletePaymentTokenError> ex)
            {
                throw Translate(ex, "DeletePaymentToken", static (DeletePaymentTokenError e, out Error o) => e.TryGetError(out o));
            }
            return true;
        });

    // ---------------------------------------------------------------- Transaction search (Case B, paged)

    public Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, string currencyCode, CancellationToken ct) =>
        Guarded("SearchTransactions", ct, async token =>
        {
            const int MaxPages = 500; // safety backstop; whole range is otherwise walked
            var transactions = new List<GatewayTransaction>();
            int page = 1;
            int totalPages = 1;
            int pagesScanned = 0;
            bool complete = true;

            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(new SearchTransactionsRequest
                    {
                        StartDate = FormatDate(from),
                        EndDate = FormatDate(to),
                        Fields = "transaction_info",
                        TransactionCurrency = currencyCode,
                        PageSize = 100,
                        Page = page
                    }, cancellationToken: token);
                }
                catch (ApiException<RawError> ex)
                {
                    var body = SafeRead(() => ex.Error.ReadAsString());
                    var status = (int)ex.StatusCode;
                    _logger.LogWarning($"PayPal SearchTransactions failed ({status}); body={body}");
                    throw new PaymentGatewayException($"PayPal transaction search failed ({status}).", status,
                        callerFault: status is >= 400 and < 500 and not 401 and not 403 and not 429,
                        debugId: ExtractDebugId(body), inner: ex);
                }

                pagesScanned++;
                totalPages = response.TotalPages ?? 1;

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        transactions.Add(new GatewayTransaction(
                            info?.TransactionId,
                            info?.TransactionStatus,
                            ParseMoney(info?.TransactionAmount),
                            info?.TransactionAmount?.CurrencyCode,
                            info?.InvoiceId,
                            info?.CustomField,
                            ParseTime(info?.TransactionInitiationDate)));
                    }
                }

                page++;
                if (page > totalPages)
                {
                    break;
                }
                if (pagesScanned >= MaxPages)
                {
                    complete = false;
                    _logger.LogWarning($"SearchTransactions hit the {MaxPages}-page safety cap; report marked incomplete.");
                    break;
                }
            }

            return new TransactionSearchResult(transactions, pagesScanned, totalPages, complete);
        });

    // ---------------------------------------------------------------- helpers

    private static PaymentSource BuildPaymentSource(CardInput? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new PaymentSource { Card = new CardRequest { VaultId = vaultId } };
        }
        if (card is not null)
        {
            return new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            };
        }
        throw new PaymentGatewayException("No funding source (card or saved card) was provided.");
    }

    private readonly record struct AuthInfo(string Id, string? Status, DateTimeOffset? ExpiresAt, decimal? Amount);

    private static AuthInfo? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null) return null;
        foreach (var unit in purchaseUnits)
        {
            var authorization = unit.Payments?.Authorizations?.FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
            if (authorization is not null)
            {
                return new AuthInfo(authorization.Id!, authorization.Status?.Value, ParseTime(authorization.ExpirationTime), ParseMoney(authorization.Amount));
            }
        }
        return null;
    }

    private static void EnsureNoChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links, IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var requiresApproval =
            (status is not null && status == OrderStatus.PayerActionRequired) ||
            (links?.Any(l => l.Rel is "payer-action" or "approve") ?? false);

        // Only a challenge if the payment did not already complete inline (no authorization captured).
        if (requiresApproval && ExtractAuthorization(purchaseUnits) is null)
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (payer action / 3-D Secure challenge). " +
                "This integration does not perform a browser approval round-trip.");
        }
    }

    private PaymentGatewayException Translate<TError>(ApiException<TError> ex, string operation, ErrorAccessor<TError> accessor, bool renewableGuard = false)
        where TError : ApiError
    {
        var status = (int)ex.StatusCode;
        string? name = null, message = null, debugId = null;

        string? issues = null;
        if (accessor(ex.Error, out var typed))
        {
            name = typed.Name;
            message = typed.Message;
            debugId = typed.DebugId;
            if (typed.Details is { Count: > 0 })
            {
                issues = string.Join("; ", typed.Details.Select(d => $"{d.Issue}{(d.Field is null ? string.Empty : $" @ {d.Field}")}{(d.Description is null ? string.Empty : $" ({d.Description})")}"));
            }
        }
        else if (ex.Error.TryGetRawError(out var raw))
        {
            var body = SafeRead(() => raw.ReadAsString());
            message = body;
            debugId = ExtractDebugId(body);
        }

        var callerFault = status is >= 400 and < 500 and not 401 and not 403 and not 429;
        var summary = $"PayPal {operation} failed ({status})" + (name is null ? string.Empty : $": {name}")
            + (issues is null ? string.Empty : $" [{issues}]");
        _logger.LogWarning($"{summary}; message={message}; debug_id={debugId}");

        if (renewableGuard && status is 422 or 404)
        {
            return new AuthorizationNotRenewableException(
                $"The authorization can no longer be renewed to fulfil this order (PayPal {operation} {status}{(name is null ? string.Empty : $": {name}")}). " +
                "A new authorization/order is required.", status, debugId, ex);
        }

        return new PaymentGatewayException(summary + (message is null ? string.Empty : $" — {message}"), status, callerFault, debugId, ex);
    }

    private async Task<T> Guarded<T>(string operation, CancellationToken ct, Func<CancellationToken, Task<T>> call)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (PaymentGatewayException)
        {
            throw; // already translated
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException($"PayPal {operation} exceeded the {CallBudget.TotalSeconds:N0}s call budget.");
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogWarning($"PayPal {operation} returned an unprocessable response (target {ex.TargetType?.Name}).");
            throw new PaymentGatewayException($"PayPal returned a response that could not be processed during {operation}.", (int)ex.StatusCode, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogWarning($"PayPal credentials were rejected during {operation}: {ex.Message}");
            throw new PaymentGatewayException($"PayPal credentials were rejected during {operation}.", inner: ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new PaymentGatewayException($"PayPal did not respond in time during {operation}.", inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PaymentGatewayException($"Could not reach PayPal during {operation}.", inner: ex);
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value.Substring(0, max);

    private static decimal? ParseMoney(Money? money) =>
        money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto) ? dto : null;

    private static string SafeRead(Func<string> read)
    {
        try { return read(); }
        catch { return string.Empty; }
    }

    private static string? ExtractDebugId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("debug_id", out var id))
            {
                return id.GetString();
            }
        }
        catch
        {
            // not JSON — no debug id
        }
        return null;
    }
}

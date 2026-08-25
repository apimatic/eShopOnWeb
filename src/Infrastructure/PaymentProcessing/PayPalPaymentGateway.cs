using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using CoreAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using PayPalOrder = PayPalServerSdk.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

// Every PayPal call this application makes. Translates SDK exceptions into the ApplicationCore
// exception types the orchestration layer understands, and never lets a raw SDK/framework message
// reach a caller.
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            var order = await CreatePayPalOrderAsync(amount, currency, idempotencyKey, token);

            OrderAuthorizeResponse response;
            try
            {
                var body = new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = ToCardRequest(card) }
                };
                response = await _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    ct: token);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw new PaymentDeclinedException(DescribeOrdersError(ex.Error, "authorize the card"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while authorizing the card.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while authorizing the card.", ex);
            }

            return ExtractAuthorization(response);
        }, ct);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            var order = await CreatePayPalOrderAsync(amount, currency, idempotencyKey, token);

            OrderAuthorizeResponse response;
            try
            {
                var body = new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { VaultId = vaultId } }
                };
                response = await _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    ct: token);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw new PaymentDeclinedException(DescribeOrdersError(ex.Error, "authorize the saved card"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while authorizing the saved card.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while authorizing the saved card.", ex);
            }

            return ExtractAuthorization(response);
        }, ct);
    }

    private async Task<PayPalOrder> CreatePayPalOrderAsync(decimal amount, string currency, string idempotencyKey, CancellationToken token)
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
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = FormatAmount(amount) }
                    }
                }
            };

            return await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                ct: token);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw new PaymentDeclinedException(DescribeOrdersError(ex.Error, "create the order"));
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new PaymentGatewayException("PayPal was unreachable while creating the order.", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while creating the order.", ex);
        }
    }

    private static AuthorizationResult ExtractAuthorization(OrderAuthorizeResponse response)
    {
        if (response.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to complete an additional challenge (e.g. 3-D Secure) before this card can be authorized. " +
                "This integration has no browser-approval step, so the payment cannot proceed.");
        }

        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null)
        {
            throw new PaymentGatewayException("PayPal did not return an authorization for this order.");
        }

        DateTimeOffset? expiresAt = ParseDate(authorization.ExpirationTime);
        return new AuthorizationResult(response.Id ?? string.Empty, authorization.Id ?? string.Empty, authorization.Status?.Value ?? string.Empty, expiresAt);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            PaymentAuthorization auth;
            try
            {
                auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: $"reauthorize-{authorizationId}-{Guid.NewGuid():N}",
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw new AuthorizationNotRenewableException(DescribePaymentsError(ex.Error, "reauthorize"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while reauthorizing the payment.", ex);
            }
            catch (JsonException ex)
            {
                throw new AuthorizationNotRenewableException("PayPal returned a response that could not be processed while reauthorizing the payment.");
            }

            return new ReauthorizationResult(auth.Id ?? authorizationId, auth.Status?.Value ?? string.Empty, ParseDate(auth.ExpirationTime));
        }, ct);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            CapturedPayment capture;
            try
            {
                capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                // Any typed failure capturing an authorization (not found / expired / already
                // captured / declined) is treated uniformly as "this hold needs renewing" - the
                // orchestration retries once via reauthorize before giving up.
                throw new AuthorizationExpiredException(DescribePaymentsError(ex.Error, "capture"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while capturing the payment.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while capturing the payment.", ex);
            }

            var capturedAmount = ParseMoney(capture.Amount) ?? 0m;
            var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee);
            var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount);
            var capturedAt = ParseDate(capture.UpdateTime) ?? ParseDate(capture.CreateTime) ?? DateTimeOffset.UtcNow;

            return new CaptureResult(capture.Id ?? string.Empty, capture.Status?.Value ?? string.Empty, capturedAmount, fee, net, capturedAt);
        }, ct);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        await Bounded(async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: $"void-{authorizationId}-{Guid.NewGuid():N}",
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw new PaymentGatewayException(DescribePaymentsError(ex.Error, "void"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while voiding the authorization.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while voiding the authorization.", ex);
            }
        }, ct);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            PayPalServerSdk.Models.Refund refund;
            try
            {
                var body = new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) } };
                refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw new PaymentGatewayException(DescribePaymentsError(ex.Error, "refund"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while refunding the payment.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while refunding the payment.", ex);
            }

            return new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? string.Empty, ParseMoney(refund.Amount) ?? amount);
        }, ct);
    }

    public async Task<SavedCardResult> SaveCardAsync(CardDetails card, string merchantCustomerId, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            PaymentTokenResponse response;
            try
            {
                var body = new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToAddress(card.BillingAddress)
                        }
                    }
                };

                response = await _client.Vault.CreatePaymentToken(payPalRequestId: Guid.NewGuid().ToString(), body: body, ct: token);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw new PaymentDeclinedException(DescribeVaultError(ex.Error, "save the card"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while saving the card.", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while saving the card.", ex);
            }

            var savedCard = response.PaymentSource?.Card;
            var (expiryYear, expiryMonth) = ParseExpiry(savedCard?.Expiry);
            return new SavedCardResult(response.Id ?? string.Empty, savedCard?.Brand?.Value, savedCard?.LastDigits, expiryMonth, expiryYear);
        }, ct);
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await Bounded(async token =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: token);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw new PaymentGatewayException(DescribeVaultError(ex.Error, "delete the saved card"));
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                throw new PaymentGatewayException("PayPal was unreachable while deleting the saved card.", ex);
            }
        }, ct);
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return await Bounded(async token =>
        {
            var results = new List<TransactionRecord>();
            var startDate = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var endDate = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: startDate,
                        endDate: endDate,
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
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException($"PayPal transaction search failed with HTTP {(int)ex.Error.StatusCode}.", ex);
                }
                catch (Exception ex) when (IsTransportFailure(ex))
                {
                    throw new PaymentGatewayException("PayPal was unreachable while searching transactions.", ex);
                }
                catch (JsonException ex)
                {
                    throw new PaymentGatewayException("PayPal returned a response that could not be processed while searching transactions.", ex);
                }

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;

                    results.Add(new TransactionRecord(
                        info.TransactionId,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        ParseDate(info.TransactionInitiationDate)));
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);

            return (IReadOnlyList<TransactionRecord>)results;
        }, ct);
    }

    // --- request building -------------------------------------------------

    private static CardRequest ToCardRequest(CardDetails card) => new()
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static PayPalServerSdk.Models.Address ToAddress(CoreAddress address) => new()
    {
        AddressLine1 = address.Street,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.ZipCode,
        CountryCode = address.Country
    };

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    // UNVERIFIED against the generated model (CardRequest.Expiry is a plain string?) - "YYYY-MM" is
    // the format used here; confirmed empirically against the sandbox during self-verification.
    private static string FormatExpiry(int year, int month) => $"{year:D4}-{month:D2}";

    private static (int? year, int? month) ParseExpiry(string? expiry)
    {
        if (string.IsNullOrEmpty(expiry)) return (null, null);
        var parts = expiry.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
        {
            return (y, m);
        }

        return (null, null);
    }

    // --- response reading ---------------------------------------------------

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    // --- error translation ---------------------------------------------------

    private static bool IsTransportFailure(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private static string DescribeOrdersError(CreateOrderError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action}.";
        }

        return $"PayPal rejected the request to {action}.";
    }

    private static string DescribeOrdersError(AuthorizeOrderError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action}.";
        }

        return $"PayPal rejected the request to {action}.";
    }

    private static string DescribePaymentsError(ReauthorizePaymentError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return $"PayPal returned HTTP {(int)noContent.StatusCode} while trying to {action} the payment.";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action} the payment.";
        }

        return $"PayPal rejected the request to {action} the payment.";
    }

    private static string DescribePaymentsError(CaptureAuthorizedPaymentError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return $"PayPal returned HTTP {(int)noContent.StatusCode} while trying to {action} the payment.";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action} the payment.";
        }

        return $"PayPal rejected the request to {action} the payment.";
    }

    private static string DescribePaymentsError(VoidPaymentError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return $"PayPal returned HTTP {(int)noContent.StatusCode} while trying to {action} the authorization.";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action} the authorization.";
        }

        return $"PayPal rejected the request to {action} the authorization.";
    }

    private static string DescribePaymentsError(RefundCapturedPaymentError error, string action)
    {
        if (error.TryGetError(out var err))
        {
            return FormatError(err.Name, err.Message, err.DebugId, err.Details);
        }

        if (error.TryGetNoContent(out var noContent))
        {
            return $"PayPal returned HTTP {(int)noContent.StatusCode} while trying to {action} the payment.";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action} the payment.";
        }

        return $"PayPal rejected the request to {action} the payment.";
    }

    private static string DescribeVaultError(CreatePaymentTokenError error, string action)
    {
        if (error.TryGetError1(out var err))
        {
            return $"{err.Name}: {err.Message} (PayPal debug id {err.DebugId})";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action}.";
        }

        return $"PayPal rejected the request to {action}.";
    }

    private static string DescribeVaultError(DeletePaymentTokenError error, string action)
    {
        if (error.TryGetError1(out var err))
        {
            return $"{err.Name}: {err.Message} (PayPal debug id {err.DebugId})";
        }

        if (error.TryGetRawError(out var raw))
        {
            return $"PayPal returned HTTP {(int)raw.StatusCode} while trying to {action}.";
        }

        return $"PayPal rejected the request to {action}.";
    }

    private static string FormatError(string name, string message, string debugId, IReadOnlyList<ErrorDetails>? details)
    {
        if (details is null || details.Count == 0)
        {
            return $"{name}: {message} (PayPal debug id {debugId})";
        }

        var detailText = string.Join("; ", details.Select(d => d.Description is not null ? $"{d.Issue} - {d.Description}" : d.Issue));
        return $"{name}: {message} - {detailText} (PayPal debug id {debugId})";
    }

    // --- call budget ---------------------------------------------------

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException("The request to PayPal timed out.");
        }
    }

    private static async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException("The request to PayPal timed out.");
        }
    }
}

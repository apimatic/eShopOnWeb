using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;

    public PayPalService(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<PayPalAuthResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string idempotencyBase,
        PayPalCardSource card, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(amount, currency, idempotencyBase, ct);
        var authId = await AuthorizeOrderAsync(payPalOrderId, BuildCardPaymentSource(card), idempotencyBase, ct);
        return new PayPalAuthResult(payPalOrderId, authId);
    }

    public async Task<PayPalAuthResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string idempotencyBase,
        string vaultTokenId, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(amount, currency, idempotencyBase, ct);
        var source = new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest { VaultId = vaultTokenId }
        };
        var authId = await AuthorizeOrderAsync(payPalOrderId, source, idempotencyBase, ct);
        return new PayPalAuthResult(payPalOrderId, authId);
    }

    private async Task<string> CreatePayPalOrderAsync(
        decimal amount, string currency, string idempotencyBase, CancellationToken ct)
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
                        Value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    },
                    CustomId = idempotencyBase
                }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{idempotencyBase}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            return order.Id ?? throw new InvalidOperationException("PayPal did not return an order ID.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapOrderError(ex.Error, "create order");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    private async Task<string> AuthorizeOrderAsync(
        string payPalOrderId,
        OrderAuthorizeRequestPaymentSource paymentSource,
        string idempotencyBase,
        CancellationToken ct)
    {
        var body = new OrderAuthorizeRequest { PaymentSource = paymentSource };

        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: $"authorize-{idempotencyBase}",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var authId = response.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id;
            return authId ?? throw new InvalidOperationException("PayPal did not return an authorization ID.");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeError(ex.Error, "authorize order");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<(bool isExpired, bool isVoidedOrDenied)> GetAuthorizationStatusAsync(
        string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);

            var status = auth.Status;
            if (status == AuthorizationStatus.Voided || status == AuthorizationStatus.Denied)
                return (false, true);

            if (auth.ExpirationTime != null
                && DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry)
                && expiry < DateTimeOffset.UtcNow)
            {
                return (true, false);
            }

            return (false, false);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthError(ex.Error, "get authorization");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<string> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
        };

        try
        {
            var newAuth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            return newAuth.Id ?? throw new InvalidOperationException("PayPal did not return a new authorization ID.");
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthError(ex.Error, "reauthorize payment");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string captureIdempotencyKey, CancellationToken ct = default)
    {
        var body = new CaptureRequest { FinalCapture = true };

        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: captureIdempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var captureId = captured.Id ?? throw new InvalidOperationException("PayPal did not return a capture ID.");
            var breakdown = captured.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount?.Value);
            var fee = ParseMoney(breakdown?.PaypalFee?.Value);
            var net = ParseMoney(breakdown?.NetAmount?.Value);

            return new PayPalCaptureResult(captureId, gross, fee, net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCaptureError(ex.Error, "capture payment");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoidError(ex.Error, "void authorization");
        }
        catch (System.Text.Json.JsonException)
        {
            // PayPal returns 204 No Content on success; SDK may fail parsing the empty body.
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amount.Value.ToString("F2") } }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var refundId = refund.Id ?? throw new InvalidOperationException("PayPal did not return a refund ID.");
            var refundAmount = amount ?? ParseMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount?.Value);
            return new PayPalRefundResult(refundId, refundAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefundError(ex.Error, "refund payment");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        string customerId, PayPalCardSource card,
        string idempotencyKey, CancellationToken ct = default)
    {
        // PayPal customer IDs allow only alphanumeric, hyphen and underscore.
        var safeCustomerId = System.Text.RegularExpressions.Regex.Replace(customerId, "[^a-zA-Z0-9_-]", "-");
        if (safeCustomerId.Length > 256) safeCustomerId = safeCustomerId.Substring(0, 256);

        var body = new PaymentTokenRequest
        {
            Customer = new Customer { Id = safeCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildVaultAddress(card)
                }
            }
        };

        try
        {
            var result = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                requestOptions: null,
                ct: ct);

            var tokenId = result.Id ?? throw new InvalidOperationException("PayPal did not return a vault token ID.");
            var cardInfo = result.PaymentSource?.Card;

            return new PayPalVaultResult(
                tokenId,
                cardInfo?.LastDigits,
                cardInfo?.Brand?.Value,
                cardInfo?.Expiry,
                cardInfo?.Type?.Value);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapVaultError(ex.Error, "vault card");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultTokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapVaultError(ex.Error, "delete vaulted card");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> GetTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var all = new List<PayPalTransaction>();
        int page = 1;

        do
        {
            try
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    requestOptions: null,
                    ct: ct);

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        all.Add(new PayPalTransaction(
                            info?.TransactionId,
                            info?.PaypalReferenceId,
                            info?.TransactionStatus,
                            ParseMoneyNullable(info?.TransactionAmount?.Value),
                            ParseMoneyNullable(info?.FeeAmount?.Value),
                            info?.TransactionInitiationDate));
                    }
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages) break;
                page++;
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalProviderException(
                    $"PayPal transaction search failed: HTTP {(int)ex.Error.StatusCode} — {ex.Error.ReadAsString()}",
                    ex);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                throw new PayPalProviderException("PayPal is unreachable.", ex);
            }
        } while (true);

        return all;
    }

    // --- Helpers ---

    private static OrderAuthorizeRequestPaymentSource BuildCardPaymentSource(PayPalCardSource card)
    {
        Address? billing = null;
        if (card.CountryCode != null)
        {
            billing = new Address
            {
                CountryCode = card.CountryCode,
                AddressLine1 = card.AddressLine1,
                AdminArea2 = card.City,
                AdminArea1 = card.State,
                PostalCode = card.PostalCode
            };
        }

        return new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = billing
            }
        };
    }

    private static Address? BuildVaultAddress(PayPalCardSource card)
    {
        if (card.CountryCode == null) return null;
        return new Address
        {
            CountryCode = card.CountryCode,
            AddressLine1 = card.AddressLine1,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode
        };
    }

    private static decimal ParseMoney(string? value)
    {
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return 0m;
    }

    private static decimal? ParseMoneyNullable(string? value)
    {
        if (value == null) return null;
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    // --- Error mappers (Case A operations) ---

    private static string DescribeError(Error e)
    {
        var details = e.Details != null && e.Details.Count > 0
            ? " | " + string.Join("; ", System.Linq.Enumerable.Select(e.Details, d => $"{d.Field ?? d.Issue}: {d.Description ?? d.Issue}"))
            : string.Empty;
        return $"{e.Name} — {e.Message}{details} (debugId:{e.DebugId})";
    }

    private static PayPalProviderException MapOrderError(CreateOrderError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {DescribeError(e)}");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode} — {raw.ReadAsString()}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapAuthorizeError(AuthorizeOrderError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {DescribeError(e)}");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode} — {raw.ReadAsString()}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapGetAuthError(GetAuthorizedPaymentError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}");
        if (err.TryGetNoContent(out RawError nc))
            return new PayPalProviderException($"PayPal {op} internal error.");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapReauthError(ReauthorizePaymentError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}", isOperatorActionable: true);
        if (err.TryGetNoContent(out RawError nc))
            return new PayPalProviderException($"PayPal {op} internal error.");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapCaptureError(CaptureAuthorizedPaymentError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}");
        if (err.TryGetNoContent(out RawError nc))
            return new PayPalProviderException($"PayPal {op} internal error.");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapVoidError(VoidPaymentError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}");
        if (err.TryGetNoContent(out RawError nc))
            return new PayPalProviderException($"PayPal {op} internal error.");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapRefundError(RefundCapturedPaymentError err, string op)
    {
        if (err.TryGetError(out Error e))
            return new PayPalProviderException($"PayPal {op} failed: {DescribeError(e)}");
        if (err.TryGetNoContent(out RawError nc))
            return new PayPalProviderException($"PayPal {op} internal error.");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode} — {raw.ReadAsString()}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapVaultError(CreatePaymentTokenError err, string op)
    {
        if (err.TryGetError1(out Error1 e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }

    private static PayPalProviderException MapVaultError(DeletePaymentTokenError err, string op)
    {
        if (err.TryGetError1(out Error1 e))
            return new PayPalProviderException($"PayPal {op} failed: {e.Name} — {e.Message}");
        if (err.TryGetRawError(out RawError raw))
            return new PayPalProviderException($"PayPal {op} failed: HTTP {(int)raw.StatusCode}");
        return new PayPalProviderException($"PayPal {op} failed.");
    }
}

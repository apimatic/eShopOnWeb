using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// PayPal-SDK-backed implementation of <see cref="IPayPalGateway"/>. Every failure is
/// translated to <see cref="PayPalGatewayException"/> so the application layer never sees an
/// SDK type. Amounts are formatted to a fixed two-decimal-place wire string so a hold/capture/
/// refund matches the order total to the cent.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<VaultedCardResult> CreatePaymentTokenAsync(CardDetails card, string merchantCustomerId, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = NormalizeCardNumber(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(payPalRequestId: null, body: body, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ToException(ex.Error, "save card");
        }

        var cardResponse = response.PaymentSource?.Card;
        if (response.Id is null)
        {
            throw new PayPalGatewayException("PayPal saved the card but returned no vault id.", isProviderRejection: false);
        }

        return new VaultedCardResult
        {
            VaultId = response.Id,
            CardBrand = cardResponse?.Brand?.Value,
            Last4 = cardResponse?.LastDigits,
            Expiry = cardResponse?.Expiry
        };
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw ToException(ex.Error, "delete saved card");
        }
    }

    public async Task<OrderAuthorizationResult> AuthorizeAsync(decimal amount, string currency, string payPalRequestId, CardDetails? card, string? vaultId, CancellationToken ct)
    {
        // Single-step pattern: payment_source travels on CreateOrder itself (per Api/Orders.cs's
        // payPalRequestId doc-comment: "mandatory for all single-step create order calls...
        // with payment source information like Card"), so the same CardRequest is built once and
        // handed to CreateOrder; AuthorizeOrder is then called with no body.
        var cardRequest = card is not null
            ? new CardRequest
            {
                Number = NormalizeCardNumber(card.Number),
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress),
                Attributes = BuildCardAttributes(),
                StoredCredential = BuildStoredCredential()
            }
            : new CardRequest
            {
                VaultId = vaultId,
                Attributes = BuildCardAttributes(),
                StoredCredential = BuildStoredCredential()
            };

        var createBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = FormatAmount(amount) }
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        PayPalServerSdk.Models.Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: createBody,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToException(ex.Error, "authorize order");
        }

        if (created.Id is null)
        {
            throw new PayPalGatewayException("PayPal created the order but returned no order id.", isProviderRejection: false);
        }

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: created.Id,
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw ToException(ex.Error, "authorize order");
        }

        var authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PayPalGatewayException("PayPal authorized the order but returned no authorization record.", isProviderRejection: false);
        }

        return new OrderAuthorizationResult
        {
            PayPalOrderId = created.Id,
            AuthorizationId = authorization.Id,
            Status = authorization.Status?.Value ?? "UNKNOWN",
            ExpiresAt = ParseDate(authorization.ExpirationTime)
        };
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct)
    {
        var body = new ReauthorizeRequest { Amount = BuildMoney(amount, currency) };

        try
        {
            var response = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: payPalRequestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new ReauthorizationResult
            {
                AuthorizationId = response.Id ?? authorizationId,
                Status = response.Status?.Value ?? "UNKNOWN",
                ExpiresAt = ParseDate(response.ExpirationTime)
            };
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw ToException(ex.Error, "reauthorize payment");
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct)
    {
        var body = new CaptureRequest { Amount = BuildMoney(amount, currency), FinalCapture = true };

        try
        {
            var response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: payPalRequestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            if (response.Id is null)
            {
                throw new PayPalGatewayException("PayPal captured the payment but returned no capture id.", isProviderRejection: false);
            }

            var breakdown = response.SellerReceivableBreakdown;
            return new CaptureResult
            {
                CaptureId = response.Id,
                Status = response.Status?.Value ?? "UNKNOWN",
                CapturedAmount = ParseAmount(breakdown?.GrossAmount) ?? amount,
                FeeAmount = ParseAmount(breakdown?.PaypalFee),
                NetAmount = ParseAmount(breakdown?.NetAmount)
            };
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw ToException(ex.Error, "capture payment");
        }
    }

    public async Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: payPalRequestId,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw ToException(ex.Error, "void authorization");
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = amount is not null
            ? new RefundRequest { Amount = BuildMoney(amount.Value, currency) }
            : new RefundRequest();

        try
        {
            var response = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            if (response.Id is null)
            {
                throw new PayPalGatewayException("PayPal refunded the payment but returned no refund id.", isProviderRejection: false);
            }

            return new RefundResult
            {
                RefundId = response.Id,
                Status = response.Status?.Value ?? "UNKNOWN",
                Amount = ParseAmount(response.Amount) ?? amount ?? 0m,
                TotalRefundedAmount = ParseAmount(response.SellerPayableBreakdown?.TotalRefundedAmount)
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ToException(ex.Error, "refund payment");
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransactionRecord>();
        var windowStart = from;

        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;

            var page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatDate(windowStart),
                        endDate: FormatDate(windowEnd),
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        page: page,
                        ct: ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw ToException(ex.Error, "search transactions");
                }

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalTransactionRecord
                    {
                        TransactionId = info.TransactionId ?? string.Empty,
                        PayPalOrderId = info.PaypalReferenceIdType == PayPalReferenceIdType.Odr ? info.PaypalReferenceId : null,
                        Amount = ParseAmount(info.TransactionAmount),
                        CurrencyCode = info.TransactionAmount?.CurrencyCode,
                        Status = info.TransactionStatus,
                        InitiatedAt = ParseDate(info.TransactionInitiationDate)
                    });
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages)
                {
                    break;
                }
                page++;
            }

            if (windowEnd >= to)
            {
                break;
            }
            windowStart = windowEnd.AddTicks(1);
        }

        return results;
    }

    private static string NormalizeCardNumber(string number) => new string(number.Where(char.IsDigit).ToArray());

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);

    private static Money BuildMoney(decimal amount, string currency) => new Money { CurrencyCode = currency, Value = FormatAmount(amount) };

    private static CardAttributes BuildCardAttributes() => new CardAttributes
    {
        Verification = new CardVerification { Method = OrdersCardVerificationMethod.ScaWhenRequired }
    };

    private static CardStoredCredential BuildStoredCredential() => new CardStoredCredential
    {
        PaymentInitiator = PaymentInitiator.Customer,
        PaymentType = StoredPaymentSourcePaymentType.OneTime,
        Usage = StoredPaymentSourceUsageType.First
    };

    private static PayPalServerSdk.Models.Address? MapAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalServerSdk.Models.Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static decimal? ParseAmount(Money? money)
    {
        if (money?.Value is not string raw)
        {
            return null;
        }
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? ParseDate(string? raw)
    {
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
    }

    private static PayPalGatewayException ToException(CreatePaymentTokenError error, string operation)
    {
        if (error.TryGetError1(out var typed))
        {
            return FromError1(typed!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(DeletePaymentTokenError error, string operation)
    {
        if (error.TryGetError1(out var typed))
        {
            return FromError1(typed!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(CreateOrderError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(AuthorizeOrderError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(CaptureAuthorizedPaymentError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(ReauthorizePaymentError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(VoidPaymentError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(RefundCapturedPaymentError error, string operation)
    {
        if (error.TryGetError(out var typed))
        {
            return FromError(typed!, operation);
        }
        if (error.TryGetNoContent(out var noContent))
        {
            return FromRaw(noContent!, operation);
        }
        return FromRawOrUnknown(error.TryGetRawError(out var raw), raw, operation);
    }

    private static PayPalGatewayException ToException(RawError error, string operation) => FromRaw(error, operation);

    private static PayPalGatewayException FromError(Error error, string operation)
    {
        var details = (error.Details ?? Array.Empty<ErrorDetails>()).ToList();
        var issues = details.Select(d => d.Issue).Where(i => i is not null).Select(i => i!).ToList();
        var detailText = string.Join("; ", details.Select(d => $"{d.Issue}{(string.IsNullOrEmpty(d.Description) ? "" : $" ({d.Description})")}"));

        return new PayPalGatewayException(
            $"PayPal rejected the request to {operation}: {error.Name} - {error.Message}" +
            (detailText.Length > 0 ? $" [{detailText}]" : "") +
            $" (debug_id={error.DebugId})",
            isProviderRejection: true,
            debugId: error.DebugId,
            issues: issues);
    }

    private static PayPalGatewayException FromError1(Error1 error, string operation)
    {
        var details = (error.Details ?? Array.Empty<ErrorDetails1>()).ToList();
        var issues = details.Select(d => d.Issue).Where(i => i is not null).Select(i => i!).ToList();
        var detailText = string.Join("; ", details.Select(d => $"{d.Issue}{(string.IsNullOrEmpty(d.Description) ? "" : $" ({d.Description})")}"));

        return new PayPalGatewayException(
            $"PayPal rejected the request to {operation}: {error.Name} - {error.Message}" +
            (detailText.Length > 0 ? $" [{detailText}]" : "") +
            $" (debug_id={error.DebugId})",
            isProviderRejection: true,
            debugId: error.DebugId,
            issues: issues);
    }

    private static PayPalGatewayException FromRawOrUnknown(bool found, RawError? raw, string operation)
    {
        return found && raw is not null
            ? FromRaw(raw, operation)
            : new PayPalGatewayException($"PayPal request to {operation} failed with an unrecognized error shape.", isProviderRejection: false);
    }

    private static PayPalGatewayException FromRaw(RawError raw, string operation)
    {
        var status = (int)raw.StatusCode;
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch
        {
            body = "(unreadable body)";
        }

        return new PayPalGatewayException(
            $"PayPal request to {operation} failed with HTTP {status}: {body}",
            isProviderRejection: status is >= 400 and < 500);
    }
}

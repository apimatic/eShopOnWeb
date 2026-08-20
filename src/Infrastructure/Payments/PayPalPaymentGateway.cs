using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.Models;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalApiClient _client;

    public PayPalPaymentGateway(PayPalApiClient client)
    {
        _client = client;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = MapCard(card)
        };
        return AuthorizeAsync(invoiceId, customId, amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = new PayPalCardRequest
            {
                VaultId = vaultId,
                StoredCredential = new PayPalCardStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            }
        };
        return AuthorizeAsync(invoiceId, customId, amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = currency,
                Value = PayPalJson.FormatAmount(amount, currency)
            },
            FinalCapture = true,
            InvoiceId = invoiceId
        };

        var capture = await _client.SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            request,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(capture.Id))
        {
            throw new PaymentException("PayPal captured the payment but did not return a capture id.", 502);
        }

        var captureId = capture.Id;
        if (capture.SellerReceivableBreakdown == null)
        {
            capture = await _client.SendAsync<PayPalCaptureDto>(
                HttpMethod.Get,
                $"v2/payments/captures/{Uri.EscapeDataString(captureId)}",
                body: null,
                paypalRequestId: null,
                cancellationToken);
            captureId = capture.Id ?? captureId;
        }

        var capturedAmount = PayPalJson.ParseAmount(capture.Amount?.Value ?? capture.SellerReceivableBreakdown?.GrossAmount?.Value);
        var fee = capture.SellerReceivableBreakdown?.PaypalFee?.Value;
        var net = capture.SellerReceivableBreakdown?.NetAmount?.Value;

        return new CaptureResult(
            captureId,
            capture.Status ?? "COMPLETED",
            capturedAmount,
            fee == null ? null : PayPalJson.ParseAmount(fee),
            net == null ? null : PayPalJson.ParseAmount(net),
            capture.Amount?.CurrencyCode ?? currency);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest
        {
            Amount = new PayPalMoneyDto
            {
                CurrencyCode = currency,
                Value = PayPalJson.FormatAmount(amount, currency)
            }
        };

        var authorization = await _client.SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            request,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new AuthorizationUnrenewableException(
                "PayPal could not renew the payment hold. Ask the shopper to pay again.");
        }

        return new AuthorizationResult(
            PayPalOrderId: string.Empty,
            PayPalOrderStatus: authorization.Status ?? "CREATED",
            AuthorizationId: authorization.Id,
            AuthorizationStatus: authorization.Status ?? "CREATED",
            AuthorizedAmount: PayPalJson.ParseAmount(authorization.Amount?.Value),
            Currency: authorization.Amount?.CurrencyCode ?? currency,
            ExpirationTime: ParseTimestamp(authorization.ExpirationTime));
    }

    public Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return _client.SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: null,
            paypalRequestId: idempotencyKey,
            cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        PayPalRefundRequest? request = null;
        if (amount.HasValue)
        {
            request = new PayPalRefundRequest
            {
                Amount = new PayPalMoneyDto
                {
                    CurrencyCode = currency,
                    Value = PayPalJson.FormatAmount(amount.Value, currency)
                }
            };
        }

        var refund = await _client.SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request ?? new PayPalRefundRequest(),
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refund.Id))
        {
            throw new PaymentException("PayPal refunded the capture but did not return a refund id.", 502);
        }

        return new RefundResult(
            refund.Id,
            refund.Status ?? "COMPLETED",
            PayPalJson.ParseAmount(refund.Amount?.Value ?? (amount.HasValue ? PayPalJson.FormatAmount(amount.Value, currency) : "0")),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<(string Status, DateTimeOffset? ExpirationTime)> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _client.SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            paypalRequestId: null,
            cancellationToken);

        return (authorization.Status ?? "UNKNOWN", ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string? merchantCustomerId,
        string? paypalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            Customer = BuildCustomer(merchantCustomerId, paypalCustomerId),
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = MapCard(card)
            }
        };

        var setup = await _client.SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupRequest,
            $"{idempotencyKey}-setup",
            cancellationToken);

        EnsureNoPayerActionRequired(setup.Status, setup.Links, "saving the card");

        if (string.IsNullOrWhiteSpace(setup.Id))
        {
            throw new PaymentException("PayPal did not return a setup token for the card.", 502);
        }

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(setup.Status) &&
            !string.Equals(setup.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            // CREATED can still be vaulted immediately for direct card setup tokens.
        }

        var tokenRequest = new PayPalPaymentTokenRequest
        {
            Customer = BuildCustomer(merchantCustomerId, setup.Customer?.Id ?? paypalCustomerId),
            PaymentSource = new PayPalPaymentTokenSource
            {
                Token = new PayPalVaultTokenRequest
                {
                    Id = setup.Id,
                    Type = "SETUP_TOKEN"
                }
            }
        };

        var token = await _client.SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenRequest,
            $"{idempotencyKey}-token",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new PaymentException("PayPal did not return a payment token for the saved card.", 502);
        }

        var cardResponse = token.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        var lastDigits = cardResponse?.LastDigits;
        if (string.IsNullOrWhiteSpace(lastDigits))
        {
            lastDigits = LastDigitsFromPan(card.Number);
        }

        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id ?? paypalCustomerId,
            lastDigits,
            cardResponse?.Brand ?? InferBrand(card.Number),
            cardResponse?.Expiry ?? card.Expiry,
            cardResponse?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SendAsync(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
                body: null,
                paypalRequestId: null,
                cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal.
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to, TimeSpan.FromDays(30)))
        {
            var page = 1;
            int totalPages;
            do
            {
                var query =
                    $"start_date={Uri.EscapeDataString(FormatReportingTimestamp(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportingTimestamp(windowEnd))}" +
                    $"&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

                var response = await _client.SendAsync<PayPalSearchResponse>(
                    HttpMethod.Get,
                    $"v1/reporting/transactions?{query}",
                    body: null,
                    paypalRequestId: null,
                    cancellationToken);

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null)
                        {
                            continue;
                        }

                        results.Add(new GatewayTransaction(
                            info.TransactionId ?? string.Empty,
                            info.PaypalReferenceId,
                            info.PaypalReferenceIdType,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            info.TransactionAmount == null ? null : PayPalJson.ParseAmount(info.TransactionAmount.Value),
                            info.FeeAmount == null ? null : PayPalJson.ParseAmount(info.FeeAmount.Value),
                            info.TransactionAmount?.CurrencyCode,
                            info.InvoiceId,
                            info.CustomField,
                            info.InstrumentType,
                            ParseTimestamp(info.TransactionInitiationDate)));
                    }
                }

                totalPages = response.TotalPages ?? page;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        PayPalPaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var createRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    Amount = new PayPalAmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = PayPalJson.FormatAmount(amount, currency)
                    },
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {customId}"
                }
            }
        };

        var created = await _client.SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            createRequest,
            $"{idempotencyKey}-create",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(created.Id))
        {
            throw new PaymentException("PayPal did not return an order id for the payment hold.", 502);
        }

        EnsureNoPayerActionRequired(created.Status, created.Links, "authorizing the payment");

        var authorized = await _client.SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(created.Id)}/authorize",
            new PayPalAuthorizeRequest { PaymentSource = paymentSource },
            $"{idempotencyKey}-authorize",
            cancellationToken);

        EnsureNoPayerActionRequired(authorized.Status, authorized.Links, "authorizing the payment");

        var authorization = authorized.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault();

        if (authorization == null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PaymentException("PayPal did not return an authorization for the order.", 502);
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException($"PayPal declined the payment hold ({authorization.Status}).", 402);
        }

        return new AuthorizationResult(
            created.Id,
            authorized.Status ?? created.Status ?? "COMPLETED",
            authorization.Id,
            authorization.Status ?? "CREATED",
            PayPalJson.ParseAmount(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? currency,
            ParseTimestamp(authorization.ExpirationTime));
    }

    private static PayPalCardRequest MapCard(CardPaymentSource card)
    {
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        var securityCode = new string((card.SecurityCode ?? string.Empty).Where(char.IsDigit).ToArray());

        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = securityCode,
            BillingAddress = card.BillingAddress == null
                ? new PayPalAddressDto
                {
                    AddressLine1 = "1 Main St",
                    AdminArea2 = "San Jose",
                    AdminArea1 = "CA",
                    PostalCode = "95131",
                    CountryCode = "US"
                }
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                        ? "US"
                        : card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalVaultCustomer? BuildCustomer(string? merchantCustomerId, string? paypalCustomerId)
    {
        if (string.IsNullOrWhiteSpace(merchantCustomerId) && string.IsNullOrWhiteSpace(paypalCustomerId))
        {
            return null;
        }

        return new PayPalVaultCustomer
        {
            Id = string.IsNullOrWhiteSpace(paypalCustomerId) ? null : paypalCustomerId,
            MerchantCustomerId = merchantCustomerId
        };
    }

    private static void EnsureNoPayerActionRequired(string? status, IEnumerable<PayPalLinkDto>? links, string action)
    {
        var hasPayerActionLink = links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerActionLink)
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper to approve {action} in a browser (3-D Secure / payer-action). " +
                "This integration does not implement a browser round-trip.");
        }
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatReportingTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxWindow)
    {
        var cursor = from;
        do
        {
            var windowEnd = cursor + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            if (windowEnd >= to)
            {
                yield break;
            }

            cursor = windowEnd;
        } while (true);
    }

    private static string LastDigitsFromPan(string number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return digits;
        }

        return digits[^4..];
    }

    private static string InferBrand(string number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith('4')) return "VISA";
        if (digits.StartsWith('5')) return "MASTERCARD";
        if (digits.StartsWith("34") || digits.StartsWith("37")) return "AMEX";
        if (digits.StartsWith('6')) return "DISCOVER";
        return "UNKNOWN";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// IPaymentGateway implementation over the PayPal REST APIs, built strictly against the
/// OpenAPI specifications in api-specs/paypal:
///  - checkout_orders_v2:      POST /v2/checkout/orders, POST /v2/checkout/orders/{id}/authorize
///  - payments_payment_v2:     authorizations get/capture/reauthorize/void, captures refund
///  - vault_payment_tokens_v3: POST/DELETE /v3/vault/payment-tokens
///  - transaction_search_v1:   GET /v1/reporting/transactions
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private readonly PayPalHttpClient _client;

    public PayPalGateway(PayPalHttpClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeCardPaymentAsync(
        string referenceId,
        string invoiceId,
        GatewayMoney amount,
        GatewayCard? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (card == null && vaultTokenId == null)
        {
            throw new ArgumentException("Either full card details or a vault token id must be supplied.");
        }

        var cardDto = card != null
            ? new CardRequestDto
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = ToDto(card.BillingAddress)
            }
            : new CardRequestDto
            {
                VaultId = vaultTokenId,
                StoredCredential = new StoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            };

        var orderRequest = new OrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new PurchaseUnitRequestDto
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    InvoiceId = invoiceId,
                    Amount = ToDto(amount)
                }
            },
            PaymentSource = new PaymentSourceRequestDto { Card = cardDto }
        };

        var order = await _client.SendAsync<OrderResponseDto>(HttpMethod.Post, "v2/checkout/orders", orderRequest, idempotencyKey, cancellationToken)
            ?? throw new PaymentDeclinedException("PayPal returned an empty response when creating the order.");

        if (order.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PaymentDeclinedException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge); " +
                "this integration only supports direct card payments without a browser step.");
        }

        // A single-step create order call carrying the payment source is authorized
        // inline by PayPal; otherwise the order must be authorized explicitly.
        var authorization = order.PurchaseUnits?.SelectMany(p => p.Payments?.Authorizations ?? new List<AuthorizationDto>()).FirstOrDefault();
        if (authorization == null)
        {
            var authorized = await _client.SendAsync<OrderResponseDto>(HttpMethod.Post, $"v2/checkout/orders/{order.Id}/authorize", new { }, $"{idempotencyKey}:authorize", cancellationToken)
                ?? throw new PaymentDeclinedException("PayPal returned an empty response when authorizing the order.");

            authorization = authorized.PurchaseUnits?.SelectMany(p => p.Payments?.Authorizations ?? new List<AuthorizationDto>()).FirstOrDefault()
                ?? throw new PaymentDeclinedException($"PayPal authorized order {order.Id} but returned no authorization resource (order status {authorized.Status}).");
        }

        return new GatewayAuthorization(
            order.Id,
            authorization.Id!,
            authorization.Status ?? "UNKNOWN",
            ToGateway(authorization.Amount),
            authorization.ExpirationTime);
    }

    public async Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var dto = await _client.SendAsync<AuthorizationDto>(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", cancellationToken: cancellationToken)
            ?? throw new PaymentDeclinedException($"PayPal returned an empty response for authorization {authorizationId}.");

        return new GatewayAuthorizationStatus(dto.Id!, dto.Status ?? "UNKNOWN", ToGateway(dto.Amount), dto.ExpirationTime);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var dto = await _client.SendAsync<AuthorizationDto>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/reauthorize",
                new ReauthorizeRequestDto { Amount = ToDto(amount) },
                idempotencyKey,
                cancellationToken)
            ?? throw new PaymentDeclinedException($"PayPal returned an empty response when reauthorizing {authorizationId}.");

        return new GatewayAuthorization(null, dto.Id!, dto.Status ?? "UNKNOWN", ToGateway(dto.Amount), dto.ExpirationTime);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, GatewayMoney amount, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequestDto
        {
            Amount = ToDto(amount),
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var dto = await _client.SendAsync<CaptureDto>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/capture",
                request,
                idempotencyKey,
                cancellationToken)
            ?? throw new PaymentDeclinedException($"PayPal returned an empty response when capturing authorization {authorizationId}.");

        return new GatewayCapture(
            dto.Id!,
            dto.Status ?? "UNKNOWN",
            ToGateway(dto.Amount)!,
            ToGateway(dto.SellerReceivableBreakdown?.GrossAmount),
            ToGateway(dto.SellerReceivableBreakdown?.PayPalFee),
            ToGateway(dto.SellerReceivableBreakdown?.NetAmount));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await _client.SendAsync<object>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", body: null, requestId: idempotencyKey, cancellationToken: cancellationToken);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, GatewayMoney? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequestDto
        {
            Amount = amount != null ? ToDto(amount) : null,
            CustomId = idempotencyKey,
            NoteToPayer = noteToPayer
        };

        var dto = await _client.SendAsync<RefundDto>(
                HttpMethod.Post,
                $"v2/payments/captures/{captureId}/refund",
                request,
                idempotencyKey,
                cancellationToken)
            ?? throw new PaymentDeclinedException($"PayPal returned an empty response when refunding capture {captureId}.");

        return new GatewayRefund(dto.Id!, dto.Status ?? "UNKNOWN", ToGateway(dto.Amount));
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(string customerId, GatewayCard card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new VaultTokenRequestDto
        {
            Customer = new VaultCustomerDto { Id = customerId },
            PaymentSource = new VaultPaymentSourceRequestDto
            {
                Card = new VaultCardRequestDto
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToDto(card.BillingAddress)
                }
            }
        };

        var dto = await _client.SendAsync<VaultTokenResponseDto>(HttpMethod.Post, "v3/vault/payment-tokens", request, idempotencyKey, cancellationToken)
            ?? throw new PaymentDeclinedException("PayPal returned an empty response when vaulting the card.");

        var cardResponse = dto.PaymentSource?.Card;
        return new GatewayVaultedCard(dto.Id!, cardResponse?.Brand, cardResponse?.LastDigits, cardResponse?.Expiry, cardResponse?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SendAsync<object>(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultTokenId}", cancellationToken: cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; deletion is idempotent in effect.
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        const int pageSize = 100; // spec: page_size maximum is 500, default 100

        var page = 1;
        while (true)
        {
            var path = "v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatInstant(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatInstant(to))}" +
                $"&fields=all&page_size={pageSize}&page={page}";

            var response = await _client.SendAsync<TransactionSearchResponseDto>(HttpMethod.Get, path, cancellationToken: cancellationToken)
                ?? throw new PaymentDeclinedException("PayPal returned an empty transaction search response.");

            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetailDto>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId == null)
                {
                    continue;
                }

                results.Add(new GatewayTransaction(
                    info.TransactionId,
                    info.PaypalReferenceId,
                    info.TransactionEventCode,
                    info.TransactionStatus,
                    ToGateway(info.TransactionAmount),
                    ToGateway(info.FeeAmount),
                    info.InvoiceId,
                    info.CustomField,
                    info.TransactionInitiationDate,
                    info.TransactionUpdatedDate));
            }

            // Cover the whole range, not just the first page.
            if (response.TotalPages <= page || (response.TransactionDetails?.Count ?? 0) == 0)
            {
                break;
            }
            page++;
        }

        return results;
    }

    private static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static MoneyDto ToDto(GatewayMoney money) =>
        new MoneyDto { CurrencyCode = money.CurrencyCode, Value = money.Value };

    private static GatewayMoney? ToGateway(MoneyDto? money) =>
        money == null ? null : new GatewayMoney(money.CurrencyCode ?? string.Empty, money.Value ?? string.Empty);

    private static AddressDto? ToDto(GatewayAddress? address) =>
        address == null
            ? null
            : new AddressDto
            {
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AdminArea2 = address.City,
                AdminArea1 = address.State,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode
            };
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly PayPalOptions _options;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        IPayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new PaymentValidationException("PayPal:Currency is not configured.");
            }

            return _options.Currency.Trim().ToUpperInvariant();
        }
    }

    public Task<PayPalOrderAuthorization> AuthorizeCardPaymentAsync(
        PayPalAuthorizeCardCommand command,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = ToCardRequest(command.Card)
        };
        return CreateAndAuthorizeAsync(command.OrderId, command.Amount, command.Currency, command.RequestId, command.InvoiceId, paymentSource, cancellationToken);
    }

    public Task<PayPalOrderAuthorization> AuthorizeVaultedCardAsync(
        PayPalAuthorizeVaultedCardCommand command,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = new PayPalCardRequestDto
            {
                VaultId = command.VaultId,
                StoredCredential = new PayPalStoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            }
        };
        return CreateAndAuthorizeAsync(command.OrderId, command.Amount, command.Currency, command.RequestId, command.InvoiceId, paymentSource, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body: null,
            requestId: null,
            cancellationToken);
        return ToAuthorizationDetails(dto);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new PayPalReauthorizeRequestDto
            {
                Amount = new PayPalMoneyDto
                {
                    CurrencyCode = currency,
                    Value = PayPalJson.FormatMoney(amount, currency)
                }
            },
            requestId,
            cancellationToken);
        return ToAuthorizationDetails(dto);
    }

    public async Task<PayPalCaptureDetails> CaptureAuthorizationAsync(
        string authorizationId,
        string requestId,
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new PayPalCaptureRequestDto
            {
                InvoiceId = invoiceId,
                FinalCapture = true
            },
            requestId,
            cancellationToken);
        return ToCaptureDetails(dto);
    }

    public async Task<PayPalCaptureDetails> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Get,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            body: null,
            requestId: null,
            cancellationToken);
        return ToCaptureDetails(dto);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: null,
            requestId: null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<PayPalRefundDetails> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        string? customId,
        CancellationToken cancellationToken = default)
    {
        object? body = null;
        if (amount.HasValue || !string.IsNullOrWhiteSpace(customId))
        {
            body = new PayPalRefundRequestDto
            {
                Amount = amount.HasValue
                    ? new PayPalMoneyDto
                    {
                        CurrencyCode = currency,
                        Value = PayPalJson.FormatMoney(amount.Value, currency)
                    }
                    : null,
                CustomId = customId
            };
        }

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            requestId,
            cancellationToken);

        return new PayPalRefundDetails
        {
            RefundId = dto.Id ?? throw new PayPalGatewayException("PayPal refund response did not include an id."),
            Status = dto.Status ?? string.Empty,
            Amount = PayPalJson.ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        PayPalVaultCardCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalVaultRequestDto
        {
            Customer = new PayPalVaultCustomerDto { Id = command.PayPalCustomerId },
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = ToCardRequest(command.Card)
            }
        };

        var dto = await SendAsync<PayPalVaultResponseDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            request,
            command.RequestId,
            cancellationToken);

        EnsureNoPayerAction(dto.Status, dto.Links, "save a card");

        var card = dto.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            VaultId = dto.Id ?? throw new PayPalGatewayException("PayPal vault response did not include a payment token id."),
            LastDigits = card?.LastDigits,
            Brand = card?.Brand,
            Expiry = card?.Expiry,
            CardholderName = card?.Name,
            PayPalCustomerId = dto.Customer?.Id ?? command.PayPalCustomerId
        };
    }

    public async Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}",
            body: null,
            requestId: null,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (chunkFrom, chunkTo) in SplitInto31DayChunks(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = Uri.EscapeDataString(PayPalJson.FormatDateTime(chunkFrom));
                var end = Uri.EscapeDataString(PayPalJson.FormatDateTime(chunkTo));
                var path =
                    $"v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

                var pageResult = await SendAsync<PayPalSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                if (pageResult.TransactionDetails is not null)
                {
                    foreach (var detail in pageResult.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId,
                            PayPalReferenceId = info.PaypalReferenceId,
                            PayPalReferenceIdType = info.PaypalReferenceIdType,
                            EventCode = info.TransactionEventCode,
                            Status = info.TransactionStatus,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            Amount = string.IsNullOrWhiteSpace(info.TransactionAmount?.Value)
                                ? null
                                : PayPalJson.ParseMoney(info.TransactionAmount.Value),
                            Currency = info.TransactionAmount?.CurrencyCode,
                            Fee = string.IsNullOrWhiteSpace(info.FeeAmount?.Value)
                                ? null
                                : PayPalJson.ParseMoney(info.FeeAmount.Value),
                            InitiationDate = PayPalJson.ParseDateTime(info.TransactionInitiationDate)
                        });
                    }
                }

                totalPages = pageResult.TotalPages > 0 ? pageResult.TotalPages : 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalOrderAuthorization> CreateAndAuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        string requestId,
        string invoiceId,
        PayPalPaymentSourceDto paymentSource,
        CancellationToken cancellationToken)
    {
        var createRequest = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequestDto
                {
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = invoiceId,
                    Amount = new PayPalMoneyDto
                    {
                        CurrencyCode = currency,
                        Value = PayPalJson.FormatMoney(amount, currency)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            createRequest,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(order.Status, order.Links, "pay for an order");

        var authorization = FirstAuthorization(order);
        if (authorization is null &&
            !string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            order = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"v2/checkout/orders/{Uri.EscapeDataString(order.Id ?? string.Empty)}/authorize",
                new PayPalAuthorizeOrderRequestDto { PaymentSource = paymentSource },
                $"{requestId}-auth",
                cancellationToken);
            EnsureNoPayerAction(order.Status, order.Links, "pay for an order");
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PayPalGatewayException(
                $"PayPal did not return an authorization for order {orderId} (PayPal order {order.Id}, status {order.Status}).");
        }

        return new PayPalOrderAuthorization
        {
            PayPalOrderId = order.Id ?? string.Empty,
            PayPalOrderStatus = order.Status ?? string.Empty,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status ?? string.Empty,
            AuthorizedAmount = PayPalJson.ParseMoney(authorization.Amount?.Value),
            Currency = authorization.Amount?.CurrencyCode ?? currency,
            CreatedAt = PayPalJson.ParseDateTime(authorization.CreateTime),
            ExpiresAt = PayPalJson.ParseDateTime(authorization.ExpirationTime)
        };
    }

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderDto order)
        => order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static void EnsureNoPayerAction(string? status, IEnumerable<PayPalLinkDto>? links, string action)
    {
        var payerActionLink = links?.Any(l =>
            l.Rel is not null &&
            l.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            payerActionLink == true)
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper browser challenge to {action}. This integration does not collect payer approval in a browser.");
        }
    }

    private static PayPalCardRequestDto ToCardRequest(PayPalCardDetails card)
    {
        return new PayPalCardRequestDto
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto dto)
    {
        return new PayPalAuthorizationDetails
        {
            AuthorizationId = dto.Id ?? throw new PayPalGatewayException("PayPal authorization response did not include an id."),
            Status = dto.Status ?? string.Empty,
            Amount = PayPalJson.ParseMoney(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? string.Empty,
            CreatedAt = PayPalJson.ParseDateTime(dto.CreateTime),
            ExpiresAt = PayPalJson.ParseDateTime(dto.ExpirationTime)
        };
    }

    private static PayPalCaptureDetails ToCaptureDetails(PayPalCaptureDto dto)
    {
        var breakdown = dto.SellerReceivableBreakdown;
        return new PayPalCaptureDetails
        {
            CaptureId = dto.Id ?? throw new PayPalGatewayException("PayPal capture response did not include an id."),
            Status = dto.Status ?? string.Empty,
            CapturedAmount = PayPalJson.ParseMoney(dto.Amount?.Value ?? breakdown?.GrossAmount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty,
            PayPalFee = string.IsNullOrWhiteSpace(breakdown?.PaypalFee?.Value)
                ? null
                : PayPalJson.ParseMoney(breakdown!.PaypalFee!.Value),
            NetAmount = string.IsNullOrWhiteSpace(breakdown?.NetAmount?.Value)
                ? null
                : PayPalJson.ParseMoney(breakdown!.NetAmount!.Value),
            CreateTime = PayPalJson.ParseDateTime(dto.CreateTime)
        };
    }

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitInto31DayChunks(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var cursor = from;
        while (cursor < to)
        {
            var chunkEnd = cursor.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (cursor, chunkEnd);
            cursor = chunkEnd;
        }

        if (from == to)
        {
            yield return (from, to);
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmpty = false)
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, PayPalJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(responseBody))
            {
                return default!;
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                if (allowEmpty)
                {
                    return default!;
                }

                throw new PayPalGatewayException($"PayPal returned {(int)response.StatusCode} with an empty body for {method} {relativePath}.");
            }

            var parsed = JsonSerializer.Deserialize<T>(responseBody, PayPalJson.Options);
            if (parsed is null)
            {
                throw new PayPalGatewayException($"PayPal returned an unreadable response for {method} {relativePath}.");
            }

            return parsed;
        }

        throw ToGatewayException(response, responseBody, relativePath);
    }

    private PayPalGatewayException ToGatewayException(HttpResponseMessage response, string responseBody, string path)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(responseBody, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Fall through with a generic message. Never include the raw body: it may echo card fields.
        }

        var details = error?.Details?
            .Select(d =>
            {
                var value = ShouldRedact(d.Field) ? "[redacted]" : d.Value;
                return $"{d.Issue}: {d.Description} ({d.Field}={value})";
            })
            .ToList() ?? new List<string>();

        var message = error?.Message ?? "PayPal request failed.";
        if (details.Count > 0)
        {
            message = $"{message} {string.Join("; ", details)}";
        }

        _logger.LogWarning(
            "PayPal call {Path} failed with {Status} name={Name} debugId={DebugId}.",
            path,
            (int)response.StatusCode,
            error?.Name,
            error?.DebugId);

        return new PayPalGatewayException(message)
        {
            HttpStatus = (int)response.StatusCode,
            PayPalDebugId = error?.DebugId,
            PayPalName = error?.Name
        };
    }

    private static bool ShouldRedact(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return false;
        }

        return field.Contains("number", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("security", StringComparison.OrdinalIgnoreCase) ||
               field.Contains("cvv", StringComparison.OrdinalIgnoreCase);
    }
}

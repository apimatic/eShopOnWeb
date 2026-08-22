using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    public const string HttpClientName = "PayPal";

    public static IServiceCollection AddEShopPayPal(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddTransient<PayPalWriteOnceHandler>();
        services.AddTransient<PayPalStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<PayPalWriteOnceHandler>()
            .AddHttpMessageHandler<PayPalStatusCaptureHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var options = CreateClientOptions(settings);
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<PayPalGateway>();
        services.AddScoped<ICheckoutOrderService, ApplicationCore.Services.CheckoutOrderService>();
        services.AddScoped<IOrderPaymentService, PayPalOrderPaymentService>();
        services.AddScoped<ISavedPaymentMethodService, PayPalSavedCardService>();
        services.AddScoped<IPaymentReconciliationService, PayPalReconciliationService>();

        return services;
    }

    internal static PayPalServerSdkClientOptions CreateClientOptions(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Environment) &&
            !string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PayPal:Environment values other than sandbox cannot be used. This SDK only exposes ServerEnvironment.Sandbox.");
        }

        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            },
            Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId ?? string.Empty,
                ClientSecret = settings.ClientSecret ?? string.Empty
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}

public sealed class PayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<Order> CreateAuthorizeOrderAsync(
        string payPalRequestId,
        string currency,
        string amountValue,
        string eshopOrderId,
        CancellationToken cancellationToken)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amountValue
                    },
                    CustomId = eshopOrderId,
                    InvoiceId = $"eShop-{eshopOrderId}-{Guid.NewGuid():N}"
                }
            }
        };

        try
        {
            var order = await InvokeWrite(
                ct => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);

            EnsureNoPayerAction(order.Status);
            return order;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrder(ex);
        }
    }

    public async Task<OrderAuthorizeResponse> AuthorizeOrderAsync(
        string payPalOrderId,
        string payPalRequestId,
        CardRequest card,
        CancellationToken cancellationToken)
    {
        var body = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = card
            }
        };

        try
        {
            var response = await InvokeWrite(
                ct => _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);

            EnsureNoPayerAction(response.Status);
            return response;
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeOrder(ex);
        }
    }

    public async Task<PaymentAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await InvokeRead(
                ct => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPayment(ex);
        }
    }

    public async Task<PaymentAuthorization> ReauthorizeAsync(
        string authorizationId,
        string payPalRequestId,
        string currency,
        string amountValue,
        CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money
            {
                CurrencyCode = currency,
                Value = amountValue
            }
        };

        try
        {
            return await InvokeWrite(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorize(ex);
        }
    }

    public async Task<CapturedPayment> CaptureAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true
        };

        try
        {
            return await InvokeWrite(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCapture(ex);
        }
    }

    public async Task<PaymentAuthorization> VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await InvokeWrite(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoid(ex);
        }
    }

    public async Task<Refund> RefundAsync(
        string captureId,
        string payPalRequestId,
        Money? amount,
        CancellationToken cancellationToken)
    {
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest { Amount = amount };

        try
        {
            return await InvokeWrite(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefund(ex);
        }
    }

    public async Task<PaymentTokenResponse> CreatePaymentTokenAsync(
        string payPalRequestId,
        PaymentTokenRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeWrite(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: body,
                    ct: ct),
                cancellationToken);

            return response;
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentToken(ex);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await InvokeWrite(
                async ct =>
                {
                    await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct);
                    return 0;
                },
                cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapDeletePaymentToken(ex);
        }
    }

    public async Task<IReadOnlyList<TransactionDetails>> SearchAllTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken cancellationToken)
    {
        var all = new List<TransactionDetails>();
        var page = 1;
        double? totalPages = null;
        const int pageSize = 100;

        while (true)
        {
            SearchResponse response;
            try
            {
                response = await InvokeRead(
                    ct => _client.TransactionSearch.SearchTransactions(
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
                        pageSize: pageSize,
                        page: page,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw PayPalErrorMapper.FromRaw(ex.Error);
            }

            if (response.TransactionDetails is not null)
            {
                all.AddRange(response.TransactionDetails);
            }

            var pageItemCount = response.TransactionDetails?.Count ?? 0;
            totalPages = response.TotalPages ?? totalPages;

            if (totalPages is double pages)
            {
                if (page >= pages)
                {
                    break;
                }
            }
            else if (pageItemCount < pageSize)
            {
                break;
            }

            page++;
            if (page > 1000)
            {
                break;
            }
        }

        return all;
    }

    private static void EnsureNoPayerAction(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw PayPalErrorMapper.PayerActionRequired();
        }
    }

    private async Task<T> InvokeWrite<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        using (PayPalWriteScope.Begin())
        {
            return await InvokeCore(call, cts.Token, cancellationToken);
        }
    }

    private async Task<T> InvokeRead<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await InvokeCore(call, cts.Token, cancellationToken);
    }

    private static async Task<T> InvokeCore<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken boundedToken,
        CancellationToken callerToken)
    {
        try
        {
            return await call(boundedToken);
        }
        catch (JsonException)
        {
            throw PayPalErrorMapper.FromJsonException();
        }
        catch (PayPalDuplicateSendException)
        {
            throw PayPalErrorMapper.DuplicateWrite();
        }
        catch (AuthSchemeException)
        {
            throw new ApiException("PayPal authentication failed. Check PayPal:ClientId and PayPal:ClientSecret.", 401);
        }
        catch (TaskCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw PayPalErrorMapper.Unreachable();
        }
        catch (HttpRequestException)
        {
            throw PayPalErrorMapper.Unreachable();
        }
    }

    private static ApiException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal create-order failed.", 502);
    }

    private static ApiException MapAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal authorize-order failed.", 502);
    }

    private static ApiException MapGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorMapper.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal get-authorization failed.", 502);
    }

    private static ApiException MapReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var mapped = PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
            return new PaymentOperationException(
                "This authorization cannot be renewed. Obtain a new payment authorization from the shopper before fulfilling. " + mapped.Message,
                mapped.StatusCode,
                mapped.DebugId,
                mapped.Issue);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorMapper.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal reauthorize failed.", 502);
    }

    private static ApiException MapCapture(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorMapper.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal capture failed.", 502);
    }

    private static ApiException MapVoid(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorMapper.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal void failed.", 502);
    }

    private static ApiException MapRefund(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return PayPalErrorMapper.FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal refund failed.", 502);
    }

    private static ApiException MapCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal vault failed.", 502);
    }

    private static ApiException MapDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return PayPalErrorMapper.FromError(error, PayPalLastStatus.Current ?? 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return PayPalErrorMapper.FromRaw(raw);
        }

        return new ApiException("PayPal delete payment token failed.", 502);
    }
}

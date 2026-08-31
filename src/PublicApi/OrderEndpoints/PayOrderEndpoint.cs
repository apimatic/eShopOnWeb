using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with PayPal, either with one-off card
/// details or with one of the caller's saved cards. No money moves yet.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ICurrentUser _currentUser;
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService, ICurrentUser currentUser, IOptions<PayPalSettings> payPalSettings)
    {
        _orderPaymentService = orderPaymentService;
        _currentUser = currentUser;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var payment = await _orderPaymentService.PayOrderAsync(
            request.OrderId,
            _currentUser.BuyerId,
            MapCard(request.Card),
            request.PaymentMethodId,
            _payPalSettings.Currency);

        response.OrderId = payment.OrderId;
        response.OrderStatus = "AwaitingFulfilment";
        response.PaymentId = payment.Id;
        response.AuthorizationId = payment.AuthorizationId ?? string.Empty;
        response.AuthorizationStatus = payment.AuthorizationStatus ?? string.Empty;
        response.Amount = payment.AuthorizedAmount;
        response.Currency = payment.Currency;
        response.ExpiresAt = payment.AuthorizationExpiresAt;
        return Results.Ok(response);
    }

    internal static CardDetails? MapCard(CardRequest? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new CardBillingAddress
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
}

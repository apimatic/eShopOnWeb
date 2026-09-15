using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PayOrderEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = _http.HttpContext!.RequireBuyerId();
        CardPaymentRequest? card = request.Card is null
            ? null
            : new CardPaymentRequest(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                request.Card.BillingAddress is null
                    ? null
                    : new ShippingAddressRequest(
                        request.Card.BillingAddress.Street,
                        request.Card.BillingAddress.City,
                        request.Card.BillingAddress.State,
                        request.Card.BillingAddress.Country,
                        request.Card.BillingAddress.ZipCode));

        var order = await orders.PayOrderAsync(buyerId, request.OrderId, card, request.PaymentMethodId);
        return Results.Ok(OrderApiMapper.ToResponse(order));
    }
}

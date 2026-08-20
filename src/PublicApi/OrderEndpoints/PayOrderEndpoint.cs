using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public PayOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout)
    {
        var card = request.Card is null ? null : CardPaymentMapper.ToCardDetails(request.Card);
        var order = await checkout.PayAsync(request.BuyerId!, request.OrderId, request.PaymentMethodId, card);
        return Results.Ok(OrderResponse.From(order, _paymentSettings.Currency));
    }
}

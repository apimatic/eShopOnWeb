using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest body, HttpContext http, ICheckoutPaymentService service) =>
                await HandleAsync(new PayOrderRouteRequest(orderId, body), http, service))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRouteRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(PayOrderRouteRequest request, HttpContext http, ICheckoutPaymentService service)
    {
        var order = await service.PayOrderAsync(new PayOrderRequest
        {
            OrderId = request.OrderId,
            BuyerId = http.RequireBuyerId(),
            Card = request.Body.Card?.ToSource(),
            PaymentMethodId = request.Body.PaymentMethodId
        });

        return Results.Ok(OrderResponse.From(order));
    }
}

public record PayOrderRouteRequest(int OrderId, PayOrderApiRequest Body);

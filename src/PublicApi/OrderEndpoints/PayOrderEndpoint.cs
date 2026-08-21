using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequestBody, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody request, IOrderPaymentService orders, HttpContext httpContext) =>
            {
                request ??= new PayOrderRequestBody();
                var response = new PayOrderResponse(request.CorrelationId());
                var order = await orders.PayAsync(
                    httpContext.RequireBuyerId(),
                    orderId,
                    new PayOrderRequest(request.PaymentMethodId, OrderDtoMapper.ToCardInput(request.Card)),
                    httpContext.RequestAborted);
                response.Order = OrderDtoMapper.From(order);
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequestBody request, IOrderPaymentService orders)
        => Task.FromResult(Results.BadRequest());
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, httpContext, paymentService);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        var order = await paymentService.CreateOrderAsync(buyerId, request, httpContext.RequestAborted);
        return Results.Created($"/api/orders/{order.OrderId}", order);
    }
}

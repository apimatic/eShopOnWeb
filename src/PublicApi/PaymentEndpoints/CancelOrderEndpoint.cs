using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new OrderIdRequest(orderId), httpContext, paymentService);
            })
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, HttpContext httpContext, IPaymentService paymentService)
    {
        var order = await paymentService.CancelOrderAsync(request.OrderId, httpContext.RequestAborted);
        return Results.Ok(order);
    }
}

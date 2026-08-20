using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderSmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderSmsService service) =>
            {
                var result = await service.CancelAsync(orderId);
                if (!result.IsSuccess)
                {
                    return result.ToHttpResult(_ => Results.Ok());
                }

                var notifications = await service.GetNotificationsForOrdersAsync(new[] { orderId });
                return Results.Ok(NotificationDtoMapper.ToOrderDto(result.Value, notifications));
            })
            .Produces<OrderSummaryDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderSmsService orderSmsService)
        => Task.FromResult(Results.Ok());
}

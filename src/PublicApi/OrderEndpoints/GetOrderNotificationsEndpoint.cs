using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest
{
    public int OrderId { get; set; }
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest { OrderId = orderId }, service, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopperOrderService service)
        => HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopperOrderService service, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetOrderNotificationsAsync(buyerId, request.OrderId);
        if (!result.IsSuccess)
        {
            return EndpointResultMapper.Map(result);
        }

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = result.Value.Select(ListMyOrdersEndpoint.MapNotification).ToList()
        });
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public ListOrderNotificationsRequest(int orderId) => OrderId = orderId;
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), orders, http);
            })
            .Produces<NotificationView[]>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService orders, HttpContext http)
    {
        var result = await orders.ListOrderNotificationsAsync(
            request.OrderId,
            http.User.RequireBuyerId(),
            http.User.IsAdministrator(),
            http.RequestAborted);
        return Results.Ok(result);
    }
}

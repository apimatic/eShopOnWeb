using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public NotificationDto[] Notifications { get; set; } = System.Array.Empty<NotificationDto>();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderFlowService orders) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(orderId, buyerId, orders, http.RequestAborted);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderFlowService orders) =>
        HandleAsync(orderId, string.Empty, orders, default);

    private static async Task<IResult> HandleAsync(
        int orderId,
        string buyerId,
        IOrderFlowService orders,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await orders.GetOrderNotificationsAsync(buyerId, orderId, cancellationToken);
            var response = new ListOrderNotificationsResponse
            {
                OrderId = orderId,
                Notifications = notifications.Select(NotificationDto.From).ToArray()
            };
            return Results.Ok(response);
        }
        catch (System.Exception ex)
        {
            return EndpointErrors.FromException(ex);
        }
    }
}

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IOrderNotificationService orders) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), httpContext, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService orders)
        => HandleAsync(request, null!, orders);

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, HttpContext httpContext, IOrderNotificationService orders)
    {
        var notifications = await orders.ListOrderNotificationsAsync(
            request.OrderId,
            httpContext.GetBuyerId(),
            httpContext.IsAdministrator());

        var response = new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        };
        return Results.Ok(response);
    }
}

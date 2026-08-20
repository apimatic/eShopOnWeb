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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IOrderNotificationService service) =>
            {
                return await HandleAsync(
                    new ListOrderNotificationsRequest(orderId, httpContext.User.Identity?.Name),
                    service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.GetOrderNotificationsAsync(request.BuyerId, request.OrderId);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId, string? buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public string? BuyerId { get; }
}

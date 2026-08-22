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

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public string? BuyerId { get; set; }
    public bool IsAdministrator { get; set; }

    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderMessagingService orderMessagingService) =>
            {
                var request = new ListOrderNotificationsRequest(orderId)
                {
                    BuyerId = ApiUser.BuyerId(httpContext),
                    IsAdministrator = ApiUser.IsAdministrator(httpContext)
                };
                return await HandleAsync(request, orderMessagingService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderMessagingService orderMessagingService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await orderMessagingService.ListNotificationsAsync(
            request.OrderId,
            request.BuyerId,
            request.IsAdministrator,
            default);

        var response = new ListOrderNotificationsResponse(request.CorrelationId());
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.From));
        return Results.Ok(response);
    }
}

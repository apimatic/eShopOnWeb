using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }
    public ListOrderNotificationsRequest(int orderId) => OrderId = orderId;
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderFulfillmentService fulfillmentService) =>
            {
                var buyerId = EndpointIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var notifications = await fulfillmentService.ListNotificationsAsync(
                        orderId,
                        buyerId,
                        EndpointIdentity.IsAdministrator(httpContext),
                        httpContext.RequestAborted);

                    return Results.Ok(new ListOrderNotificationsResponse
                    {
                        OrderId = orderId,
                        Notifications = notifications.Select(NotificationMapper.ToDto).Where(d => d != null).Cast<NotificationDto>().ToList()
                    });
                }
                catch (OrderNotFoundException)
                {
                    return Results.NotFound();
                }
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderFulfillmentService fulfillmentService)
        => Task.FromResult(Results.Ok());
}

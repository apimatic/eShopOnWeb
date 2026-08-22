using System.Collections.Generic;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderFlowService service, HttpContext http) =>
            {
                return await HandleAsync(orderId, service, http);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderFlowService service)
        => HandleAsync(orderId, service, null!);

    private async Task<IResult> HandleAsync(int orderId, IOrderFlowService service, HttpContext http)
    {
        var unauthorized = http.RequireBuyerId(out var buyerId);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        try
        {
            var notifications = await service.ListNotificationsForOrderAsync(orderId, buyerId, http.IsAdministrator());
            return Results.Ok(new ListOrderNotificationsResponse
            {
                OrderId = orderId,
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

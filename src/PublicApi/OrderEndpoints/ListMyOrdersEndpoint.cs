using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IShopperOrderService service) =>
            {
                return await HandleAsync(http.User.GetBuyerId(), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IShopperOrderService service)
    {
        var orders = await service.GetMyOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(orders.Select(o => new OrderSummaryDto
        {
            OrderId = o.Order.Id,
            Status = o.Order.Status.ToString(),
            OrderDate = o.Order.OrderDate,
            Total = o.Order.Total(),
            Notifications = o.Notifications.Select(ToDto).ToList()
        }));

        return Results.Ok(response);
    }

    internal static NotificationDto ToDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Kind = n.Kind.ToString(),
        Body = n.ContentRedacted ? null : n.Body,
        ContentRedacted = n.ContentRedacted,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ProviderDateSent = n.ProviderDateSent,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedAt = n.CreatedAt
    };
}

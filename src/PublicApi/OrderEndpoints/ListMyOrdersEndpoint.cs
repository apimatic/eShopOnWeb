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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderFlowService service, HttpContext http) =>
            {
                return await HandleAsync(service, http);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderFlowService service) => HandleAsync(service, null!);

    private async Task<IResult> HandleAsync(IOrderFlowService service, HttpContext http)
    {
        var unauthorized = http.RequireBuyerId(out var buyerId);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        var result = await service.ListMyOrdersAsync(buyerId);
        var notificationsByOrder = result.Notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        var response = new ListMyOrdersResponse
        {
            Orders = result.Orders.Select(order =>
            {
                notificationsByOrder.TryGetValue(order.Id, out var notes);
                return OrderSummaryDto.From(order, notes ?? new());
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

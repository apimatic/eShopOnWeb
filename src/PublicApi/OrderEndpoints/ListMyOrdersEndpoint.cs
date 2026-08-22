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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationWorkflow>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderNotificationWorkflow workflow) =>
            {
                return await HandleAsync(workflow);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationWorkflow workflow)
    {
        var buyerId = BuyerIdentity.GetBuyerId(_httpContextAccessor.HttpContext!.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var summaries = await workflow.ListMyOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse
        {
            Orders = summaries.Select(summary => new MyOrderDto
            {
                OrderId = summary.Order.Id,
                Status = summary.Order.Status.ToString(),
                OrderDate = summary.Order.OrderDate,
                Total = summary.Order.Total(),
                Notifications = summary.Notifications.Select(NotificationResponseMapper.ToDto).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

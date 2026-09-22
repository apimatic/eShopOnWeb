using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>The signed-in shopper's orders, each showing where its notifications got to.</summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MyOrdersResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public MyOrdersEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(
        Summary = "The signed-in shopper's orders and their notifications",
        Description = "The signed-in shopper's orders, each showing where its notifications got to",
        OperationId = "orders.mine",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<MyOrdersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var orders = await _orderNotificationService.GetOrdersForBuyerAsync(buyerId, cancellationToken);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                NotificationStatus = o.Order.NotificationStatus.ToString(),
                Notifications = o.Notifications.Select(NotificationMapper.ToDto).ToList()
            }).ToList()
        };
        return Ok(response);
    }
}

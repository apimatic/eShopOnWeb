using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's own orders, and what became of each message.
/// Delivery outcomes are refreshed from the provider best-effort (no callback URL exists).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListOrderNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<ListOrderNotificationsResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpGet("api/orders/{orderId}/notifications")]
    [SwaggerOperation(
        Summary = "Lists the notifications for one of the caller's orders",
        Description = "Lists the notifications for one of the caller's orders",
        OperationId = "orders.listNotifications",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ListOrderNotificationsResponse>> HandleAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        var notifications = await _orderNotificationService.GetOrderNotificationsAsync(buyerId, orderId, cancellationToken);

        return new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

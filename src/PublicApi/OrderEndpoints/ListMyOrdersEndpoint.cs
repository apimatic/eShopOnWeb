using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders, each showing where its notifications got to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMyOrdersResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IRepository<Notification> _notificationRepository;

    public ListMyOrdersEndpoint(IOrderNotificationService orderNotificationService,
        IRepository<Notification> notificationRepository)
    {
        _orderNotificationService = orderNotificationService;
        _notificationRepository = notificationRepository;
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(
        Summary = "Lists the caller's orders with their notifications",
        Description = "Lists the caller's orders with their notifications",
        OperationId = "orders.listMine",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ListMyOrdersResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _orderNotificationService.GetMyOrdersAsync(buyerId, cancellationToken);
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Notifications = notificationsByOrder.TryGetValue(o.Id, out var orderNotifications)
                    ? orderNotifications.Select(NotificationDto.FromEntity).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        };
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

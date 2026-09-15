using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.OrderNotifications;

namespace Microsoft.eShopWeb.PublicApi.Orders;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MyOrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly NotificationCoordinator _notifications;

    public MyOrdersController(CatalogContext db, NotificationCoordinator notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _db.Orders
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId) && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);

        var response = orders.Select(order => new MyOrderDto(
            order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).Select(NotificationDto.FromEntity).ToList()));
        return Ok(new { orders = response });
    }
}

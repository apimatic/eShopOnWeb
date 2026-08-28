using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Route("api/my-orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MyOrdersController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly OrderNotificationService _notifications;

    public MyOrdersController(CatalogContext context, OrderNotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _context.Orders
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .ThenInclude(x => x.ItemOrdered)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _context.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);

        return Ok(orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString().ToLowerInvariant(),
            total = order.Total(),
            items = order.OrderItems.Select(item => new
            {
                catalogItemId = item.ItemOrdered.CatalogItemId,
                productName = item.ItemOrdered.ProductName,
                unitPrice = item.UnitPrice,
                quantity = item.Units
            }),
            notifications = notifications.Where(x => x.OrderId == order.Id).Select(NotificationDto.From)
        }));
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IOrderNotificationService _notifications;
    private readonly TimeProvider _timeProvider;

    public OrdersController(IRepository<Order> orders, IReadRepository<CatalogItem> catalogItems,
        IOrderNotificationService notifications, TimeProvider timeProvider)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateAsync(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 || request.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            ModelState.AddModelError(nameof(request.Items), "At least one catalog item with a positive quantity is required.");
            return ValidationProblem(ModelState);
        }

        var requestedQuantities = request.Items
            .GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(requestedQuantities.Keys.ToArray()), cancellationToken);
        if (catalogItems.Count != requestedQuantities.Count)
        {
            ModelState.AddModelError(nameof(request.Items), "One or more catalog items do not exist.");
            return ValidationProblem(ModelState);
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requestedQuantities[item.Id])).ToList();
        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);
        var order = new Order(User.Identity!.Name!, address, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id));
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null) return NotFound();
        if (order.Status != OrderStatus.Placed)
            return Conflict(new { message = "Only a placed order can be dispatched." });

        order.Dispatch(_timeProvider.GetUtcNow());
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order == null) return NotFound();
        if (order.Status == OrderStatus.Cancelled)
            return Ok(new { orderId = order.Id, status = order.Status.ToString() });

        try
        {
            await _notifications.CancelPendingFollowUpsForOrderAsync(order.Id, cancellationToken);
        }
        catch (FollowUpCancellationException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider could not confirm cancellation of every scheduled follow-up; the order was not cancelled.");
        }

        order.Cancel(_timeProvider.GetUtcNow());
        await _orders.UpdateAsync(order, cancellationToken);
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<MyOrderResponse[]>> GetMyOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(User.Identity!.Name!),
            cancellationToken);
        var response = new List<MyOrderResponse>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.GetCurrentNotificationsAsync(order.Id, cancellationToken);
            response.Add(new MyOrderResponse(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
                notifications.Select(NotificationDto.FromEntity).ToArray()));
        }
        return Ok(response);
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<ActionResult<NotificationDto[]>> GetNotificationsAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(
            new OrderByOwnerAndIdWithItemsSpec(User.Identity!.Name!, orderId), cancellationToken);
        if (order == null) return NotFound();
        var notifications = await _notifications.GetCurrentNotificationsAsync(order.Id, cancellationToken);
        return Ok(notifications.Select(NotificationDto.FromEntity).ToArray());
    }
}

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    [Required]
    public OrderAddressRequest ShipToAddress { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class OrderAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    NotificationDto[] Notifications);

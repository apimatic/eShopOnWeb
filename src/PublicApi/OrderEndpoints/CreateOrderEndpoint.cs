using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities. The shopper is told by text message
/// that the order was placed (when they have a contact number on file).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _orderNotificationService;

    public CreateOrderEndpoint(IOrderService orderService, IOrderNotificationService orderNotificationService)
    {
        _orderService = orderService;
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order from catalog items",
        Description = "Places an order from catalog item ids and quantities; notifies the shopper by SMS",
        OperationId = "orders.create",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var itemQuantities = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var order = await _orderService.CreateOrderFromItemsAsync(buyerId, address, itemQuantities, cancellationToken);

        // Never fails the order: a message that cannot be sent is recorded as an outcome.
        await _orderNotificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        return new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
    }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

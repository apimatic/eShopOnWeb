using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies
/// them by SMS that the order was placed.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order",
        Description = "Places an order from catalog items for the caller",
        OperationId = "orders.create",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items == null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return BadRequest("The order must contain at least one item with a positive quantity.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()), cancellationToken);

        var missingIds = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            return BadRequest($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var shipTo = new Address(request.ShipToStreet, request.ShipToCity, request.ShipToState,
            request.ShipToCountry, request.ShipToZipCode);
        var order = new Order(User.Identity!.Name!, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Never fails the order: a messaging problem is recorded, not thrown.
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return response;
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();
    public string ShipToStreet { get; set; } = string.Empty;
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToState { get; set; } = string.Empty;
    public string ShipToCountry { get; set; } = string.Empty;
    public string ShipToZipCode { get; set; } = string.Empty;
}

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) {}
    public CreateOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

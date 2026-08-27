using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest ShipToAddress { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper and texts
/// them a confirmation if they have a contact number on file.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IReadRepository<CatalogItem> itemRepository,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, notificationService, cancellationToken);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IReadRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one item is required." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { error = "Quantities must be positive." });
        }

        var catalogItems = await itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()), cancellationToken);
        var missing = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { error = $"Unknown catalog item ids: {string.Join(", ", missing)}" });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);
        var order = new Order(user.Identity!.Name!, address, orderItems);
        order = await orderRepository.AddAsync(order, cancellationToken);

        // Notification failures never fail the order.
        await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

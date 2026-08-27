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

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them
/// by SMS that the order was placed.
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
                IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
                IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, notificationService, cancellationToken);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var response = new CreateOrderResponse(request.CorrelationId());

        var catalogItemsSpec = new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await itemRepository.ListAsync(catalogItemsSpec, cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem == null)
            {
                return Results.BadRequest($"Catalog item {item.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var shipTo = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = new Order(buyerId, shipTo, orderItems);
        order = await orderRepository.AddAsync(order, cancellationToken);

        // Notification failures never fail the order.
        await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        response.OrderId = order.Id;
        response.Total = order.Total();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

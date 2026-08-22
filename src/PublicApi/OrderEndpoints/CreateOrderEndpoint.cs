using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Extensions;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications)
    {
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IRepository<Order> orderRepository) =>
            {
                request.BuyerId = httpContext.User.GetBuyerId();
                return await HandleAsync(request, orderRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Each item must include a catalogItemId and a quantity greater than zero." });
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        if (catalogItems.Count != catalogItemIds.Length)
        {
            return Results.BadRequest(new { message = "One or more catalog items were not found." });
        }

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = request.ShipTo is { } shipTo && !string.IsNullOrWhiteSpace(shipTo.Street)
            ? new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode)
            : new Address("123 Main Street", "Seattle", "WA", "USA", "98101");

        var order = new Order(request.BuyerId, address, orderItems);
        await orderRepository.AddAsync(order);

        await _notifications.NotifyOrderPlacedAsync(order.Id, order.BuyerId);
        var sent = await _notifications.ListForOrderAsync(order.Id, refreshFromProvider: false);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = sent.Select(NotificationDto.From).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public partial class CreateOrderRequest
{
    internal string? BuyerId { get; set; }
}

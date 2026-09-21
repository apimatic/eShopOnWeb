using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the created order — a top-level field so callers can drive the flow.</summary>
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper (identity from the token), reusing the app's
/// existing Order/OrderItem model. The shopper is then told their order was placed (a messaging failure never
/// fails the order).
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "At least one order item is required." });
        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest(new { message = "Item quantities must be greater than zero." });

        var ct = http.RequestAborted;
        var itemRepository = http.RequestServices.GetRequiredService<IRepository<CatalogItem>>();
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var uriComposer = http.RequestServices.GetRequiredService<IUriComposer>();
        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        // Cross-operation invariant: every catalog item id must be one the catalog actually has.
        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), ct);
        var missing = requestedIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });

        var items = request.Items.Select(reqItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == reqItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, reqItem.Quantity);
        }).ToList();

        var a = request.ShipToAddress ?? new AddressRequest();
        var address = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await orderRepository.AddAsync(order, ct);

        // Tell the shopper their order was placed. Never throws for a messaging failure.
        await notifier.NotifyOrderPlacedAsync(order, ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

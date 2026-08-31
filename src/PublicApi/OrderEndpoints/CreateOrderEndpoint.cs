using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Places an order from catalog items. The order starts in a state awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository) =>
            {
                request.Username = OrderMapping.GetUserName(user);
                return await HandleAsync(request, orderRepository, itemRepository);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }
        if (request.Items == null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new CreateOrderResponse { Message = "The order must contain at least one item with a positive quantity." });
        }

        var catalogItems = await itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        if (catalogItems.Count != request.Items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            return Results.BadRequest(new CreateOrderResponse { Message = "One or more catalog items do not exist." });
        }

        var address = new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
            request.ShipTo.Country, request.ShipTo.ZipCode);

        var items = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(request.Username, address, items);
        await orderRepository.AddAsync(order);

        return Results.Ok(new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = OrderMapping.MapItems(order)
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest ShipTo { get; set; } = new();

    [JsonIgnore]
    public string? Username { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public string? Message { get; set; }
}

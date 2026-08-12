using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IRepository<Order> orderRepo, IRepository<CatalogItem> catalogRepo) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(request, orderRepo, catalogRepo, userId);
            })
            .Produces<CreateOrderResponse>()
            .WithName("CreateOrder")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepo, IRepository<CatalogItem> catalogRepo, string userId)
    {
        var items = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await catalogRepo.GetByIdAsync(item.CatalogItemId);
            if (catalogItem == null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found");

            var orderItem = new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                item.Quantity
            );
            items.Add(orderItem);
        }

        var address = new Address("", "", "", "", "");
        var order = new Order(userId, address, items);

        await orderRepo.AddAsync(order);
        await orderRepo.SaveChangesAsync();

        return Results.Ok(new CreateOrderResponse(order.Id.ToString()));
    }
}

public record CreateOrderRequest(List<OrderItemRequest> Items);
public record OrderItemRequest(int CatalogItemId, int Quantity);
public record CreateOrderResponse(string OrderId);

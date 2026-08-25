using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IRepository<Order> orderRepo,
                   IReadRepository<CatalogItem> catalogRepo, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity?.Name ?? "";
                return await HandleAsync(request, orderRepo, catalogRepo);
            })
            .Produces<PlaceOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<Order> orderRepo)
        => await HandleAsync(request, orderRepo, null!);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<Order> orderRepo, IReadRepository<CatalogItem> catalogRepo)
    {
        var orderItems = new List<OrderItem>();

        foreach (var item in request.Items)
        {
            var catalogItem = await catalogRepo.GetByIdAsync(item.CatalogItemId);
            if (catalogItem is null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? ""),
                catalogItem.Price,
                item.Quantity));
        }

        var address = new Address(
            request.ShipToStreet, request.ShipToCity, request.ShipToState,
            request.ShipToCountry, request.ShipToZipCode);

        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        return Results.Created($"api/orders/{order.Id}",
            new PlaceOrderResponse(request.CorrelationId()) { OrderId = order.Id });
    }
}

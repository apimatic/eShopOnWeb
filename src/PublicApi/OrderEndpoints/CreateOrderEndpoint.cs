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

/// <summary>Creates an order from catalog items. No payment is taken here.</summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request,
                IRepository<Order> orderRepo,
                IReadRepository<CatalogItem> catalogRepo,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = OrderHelpers.GetBuyerId(user);

                var catalogIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var spec = new CatalogItemsSpecification(catalogIds);
                var catalogItems = await catalogRepo.ListAsync(spec, ct);

                if (catalogItems.Count != catalogIds.Distinct().Count())
                    return Results.BadRequest(new { error = "One or more catalog items were not found." });

                var orderItems = request.Items.Select(line =>
                {
                    var ci = catalogItems.First(c => c.Id == line.CatalogItemId);
                    var pictureUri = string.IsNullOrEmpty(ci.PictureUri) ? "placeholder.png" : ci.PictureUri;
                    return new OrderItem(new CatalogItemOrdered(ci.Id, ci.Name, pictureUri), ci.Price, line.Quantity);
                }).ToList();

                var addr = request.ShipToAddress;
                var address = new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode);
                var order = new Order(buyerId, address, orderItems);
                order = await orderRepo.AddAsync(order, ct);

                return Results.Created($"api/orders/{order.Id}",
                    new CreateOrderResponse(order.Id, order.Total()));
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> itemRepository)
        => throw new System.NotImplementedException();
}

public record CreateOrderRequest(List<OrderLineItem> Items, AddressDto ShipToAddress);
public record OrderLineItem(int CatalogItemId, int Quantity);
public record AddressDto(string Street, string City, string State, string Country, string ZipCode);
public record CreateOrderResponse(int OrderId, decimal Total);

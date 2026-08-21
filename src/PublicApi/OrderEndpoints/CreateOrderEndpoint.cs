using System.Collections.Generic;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
                await HandleAsync(request with { BuyerId = BuyerIdentity.GetRequiredBuyerId(user) }, checkout))
            .Produces<OrderDetailsDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
    {
        Address? address = null;
        if (request.ShipToAddress != null)
        {
            address = new Address(
                request.ShipToAddress.Street ?? "123 Main St.",
                request.ShipToAddress.City ?? "Anytown",
                request.ShipToAddress.State ?? "CA",
                request.ShipToAddress.Country ?? "US",
                request.ShipToAddress.ZipCode ?? "12345");
        }

        var items = new List<OrderLineRequest>();
        foreach (var item in request.Items ?? new List<CreateOrderItemRequest>())
        {
            items.Add(new OrderLineRequest { CatalogItemId = item.CatalogItemId, Quantity = item.Quantity });
        }

        var created = await checkout.PlaceOrderAsync(request.BuyerId, items, address);
        return Results.Created($"api/orders/{created.OrderId}", created);
    }
}

public record CreateOrderRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public List<CreateOrderItemRequest>? Items { get; init; }
    public ShippingAddressRequest? ShipToAddress { get; init; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

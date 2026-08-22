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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout) =>
                await HandleAsync(request, user, checkout))
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
        => HandleAsync(request, new ClaimsPrincipal(), checkout);

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout)
    {
        var buyerId = CheckoutHttp.BuyerId(user);
        var lines = (request.Items ?? []).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        Address? address = request.ShipTo is null
            ? null
            : new Address(
                request.ShipTo.Street ?? "123 Main St.",
                request.ShipTo.City ?? "Seattle",
                request.ShipTo.State ?? "WA",
                request.ShipTo.Country ?? "United States",
                request.ShipTo.ZipCode ?? "98101");

        var order = await checkout.PlaceOrderAsync(buyerId, lines, address);
        return Results.Created($"api/orders/{order.Id}", CheckoutHttp.ToResponse(order));
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public CreateOrderAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

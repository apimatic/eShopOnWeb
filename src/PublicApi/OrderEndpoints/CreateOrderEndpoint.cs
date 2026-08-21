using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderShippingRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderShippingRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
                await HandleAsync(request, checkout, user))
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout) =>
        HandleAsync(request, checkout, new ClaimsPrincipal());

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user)
    {
        var items = (request.Items ?? new()).Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        PlaceOrderShipping? shipping = request.ShipTo is null
            ? null
            : new PlaceOrderShipping(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await checkout.PlaceOrderAsync(user.GetBuyerId(), items, shipping);
        var body = OrderResponseMapper.From(order);
        return Results.Created($"api/orders/{body.OrderId}", body);
    }
}

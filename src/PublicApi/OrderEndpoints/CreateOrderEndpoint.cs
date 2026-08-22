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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperCheckoutService>
{
    private readonly IPayPalGateway _payPal;

    public CreateOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IShopperCheckoutService checkout) =>
                await HandleAsync(request, user, checkout))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperCheckoutService checkout)
        => HandleAsync(request, new ClaimsPrincipal(), checkout);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IShopperCheckoutService checkout)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street ?? string.Empty,
                request.ShipTo.City ?? string.Empty,
                request.ShipTo.State ?? string.Empty,
                request.ShipTo.Country ?? "US",
                request.ShipTo.ZipCode ?? string.Empty);
        }

        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new OrderLineRequest { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var order = await checkout.PlaceOrderAsync(buyerId, lines, shipTo);
        var dto = OrderDto.From(order, _payPal.Currency);
        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = dto
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressDto? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

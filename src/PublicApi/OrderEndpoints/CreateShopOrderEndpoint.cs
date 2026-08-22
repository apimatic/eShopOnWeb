using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateShopOrderEndpoint : IEndpoint<IResult, CreateShopOrderRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateShopOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopOrderRequest request, IShopOrderService orders) =>
            {
                return await HandleAsync(request, orders);
            })
            .Produces<ShopOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateShopOrderRequest request, IShopOrderService orders)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street ?? string.Empty,
                request.ShipTo.City ?? string.Empty,
                request.ShipTo.State ?? string.Empty,
                request.ShipTo.Country ?? string.Empty,
                request.ShipTo.ZipCode ?? string.Empty);
        }

        var items = request.Items
            .Select(i => new PlaceOrderItem { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var result = await orders.PlaceAsync(buyerId, items, shipTo, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Created($"api/orders/{result.OrderId}", ShopOrderResponse.From(result, request.CorrelationId()));
    }
}

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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
    {
        var shipTo = request.ShipTo ?? new ShippingAddressRequest();
        var address = new Address(
            shipTo.Street ?? "1 Main St",
            shipTo.City ?? "San Jose",
            shipTo.State ?? "CA",
            shipTo.Country ?? "US",
            shipTo.ZipCode ?? "95131");

        var items = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await checkout.PlaceOrderAsync(request.BuyerId!, items, address, default);
        var dto = OrderPaymentDto.From(order);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = dto
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
    internal string? BuyerId { get; set; }
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

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

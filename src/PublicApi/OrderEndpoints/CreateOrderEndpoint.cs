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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateShopOrderRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopOrderRequest request, IShopOrderService orders, ClaimsPrincipal user) =>
            {
                request.BuyerId = ApiCaller.BuyerId(user);
                return await HandleAsync(request, orders);
            })
            .Produces<CreateShopOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateShopOrderRequest request, IShopOrderService orders)
    {
        Address? address = null;
        if (request.ShipToAddress != null)
        {
            address = new Address(
                request.ShipToAddress.Street ?? "123 Main St.",
                request.ShipToAddress.City ?? "Kent",
                request.ShipToAddress.State ?? "OH",
                request.ShipToAddress.Country ?? "United States",
                request.ShipToAddress.ZipCode ?? "44240");
        }

        var items = (request.Items ?? new List<CreateShopOrderItemRequest>())
            .Select(i => new PlaceOrderItem { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var order = await orders.PlaceOrderAsync(request.BuyerId, items, address, default);
        var dto = ApiCaller.ToDto(order);
        return Results.Created($"api/orders/{order.Id}", new CreateShopOrderResponse
        {
            OrderId = order.Id,
            Order = dto
        });
    }
}

public class CreateShopOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateShopOrderItemRequest>? Items { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateShopOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateShopOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

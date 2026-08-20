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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                request.BuyerId = ApiUser.GetBuyerId(user);
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        Address? address = null;
        if (request.ShipToAddress is not null)
        {
            address = new Address(
                request.ShipToAddress.Street ?? string.Empty,
                request.ShipToAddress.City ?? string.Empty,
                request.ShipToAddress.State ?? string.Empty,
                request.ShipToAddress.Country ?? "US",
                request.ShipToAddress.ZipCode ?? string.Empty);
        }

        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await checkout.PlaceOrderAsync(request.BuyerId!, lines, address);
        var body = PaymentResponseMapper.Map(order, null);
        return Results.Created($"api/orders/{body.OrderId}", new CreateOrderResponse
        {
            OrderId = body.OrderId,
            Status = body.Status,
            Total = body.Total,
            Items = body.Items
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public List<CreateOrderItemRequest>? Items { get; set; }
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
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

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}

using System.Collections.Generic;
using System.Linq;
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ICheckoutService checkout, HttpContext http) =>
            {
                request.BuyerId = http.User.RequireBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        Address? shipTo = request.ShipTo == null
            ? null
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var lines = request.Items.Select(i => new CatalogLine { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity }).ToList();
        var order = await checkout.PlaceOrderAsync(request.BuyerId!, lines, shipTo, default);

        var body = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Order = OrderResponse.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", body);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToRequest
{
    public string Street { get; set; } = "123 Main St.";
    public string City { get; set; } = "Kent";
    public string State { get; set; } = "OH";
    public string Country { get; set; } = "United States";
    public string ZipCode { get; set; } = "44240";
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public OrderResponse Order { get; set; } = new();
}

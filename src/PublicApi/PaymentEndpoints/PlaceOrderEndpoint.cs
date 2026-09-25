using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineModel> Items { get; set; } = new();

    // Set from the JWT/route in AddRoute; never bound from the request body.
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order
/// starts awaiting payment. Returns <c>orderId</c> as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext ctx) =>
            {
                request.BuyerId = CallerIdentity.BuyerId(user);
                request.CancellationToken = ctx.RequestAborted;
                return await HandleAsync(request, service);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var lines = (request.Items ?? new List<OrderLineModel>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(request.BuyerId, lines, request.CancellationToken);
        return Results.Created($"api/orders/{order.Id}", PaymentMappings.ToDto(order));
    }
}

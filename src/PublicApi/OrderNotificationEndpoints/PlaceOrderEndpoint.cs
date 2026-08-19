using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Units { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();

    [JsonIgnore]
    public string? CallerId { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the placed order (top-level).</summary>
    public int OrderId { get; set; }
}

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                request.CallerId = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new List<PlaceOrderLine>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Units))
            .ToList();

        var result = await service.PlaceOrderAsync(request.CallerId!, lines, CancellationToken.None);
        if (!result.Success)
            return Results.BadRequest(new { message = result.Error });

        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = result.OrderId };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}

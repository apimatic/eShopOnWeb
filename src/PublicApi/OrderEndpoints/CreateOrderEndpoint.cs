using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items (reusing the existing order model),
/// then tells the shopper their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderMessagingService _orderMessagingService;

    public CreateOrderEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "An order must contain at least one item." });

        var lines = request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();

        using var cts = EndpointBudget.Start();
        try
        {
            var orderId = await _orderMessagingService.PlaceOrderAsync(buyerId, lines, cts.Token);
            var response = new CreateOrderResponse(request.CorrelationId()) { OrderId = orderId };
            return Results.Created($"api/orders/{orderId}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

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

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities. The shopper is
/// told their order was placed (best-effort; a messaging failure never fails placement).
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService placementService) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                request.BuyerId = buyerId;
                return await HandleAsync(request, placementService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService placementService)
    {
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "An order must contain at least one item." });

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        try
        {
            var order = await placementService.PlaceOrderAsync(request.BuyerId!, lines);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    internal string? BuyerId { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

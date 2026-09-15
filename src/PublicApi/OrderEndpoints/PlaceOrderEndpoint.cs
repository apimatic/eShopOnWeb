using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model. The shopper is told their order was placed (best-effort).
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var ownerId = user.GetUserName();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "An order must contain at least one item." });

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        try
        {
            var orderId = await service.PlaceOrderAsync(ownerId, lines);
            var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = orderId };
            return Results.Created($"api/orders/{orderId}", response);
        }
        catch (OrderCreationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the order just placed.</summary>
    public int OrderId { get; set; }
}

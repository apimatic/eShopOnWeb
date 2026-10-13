using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities, taking the ordered quantities off eShop's
/// own stock. Available to any signed-in shopper. Does not call Walmart.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService, CancellationToken>
{
    // The API order carries no shipping address; the domain Order requires one, so a placeholder is
    // recorded. (The storefront flow carries a real address from the basket.)
    private static readonly Address PlaceholderAddress =
        new("N/A", "N/A", "N/A", "N/A", "N/A");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService service, CancellationToken ct) =>
            {
                request.BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var items = request.Items
            .Select(i => (i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var orderId = await service.PlaceOrderAsync(request.BuyerId, PlaceholderAddress, items, ct);
            return Results.Ok(new CreateOrderResponse { OrderId = orderId });
        }
        catch (InsufficientStockException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}

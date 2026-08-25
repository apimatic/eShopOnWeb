using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids/quantities for the signed-in shopper. The order
/// starts in status AwaitingPayment - see PayOrderEndpoint to authorize payment for it.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, (IOrderService OrderService, ClaimsPrincipal User)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, (orderService, user));
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, (IOrderService OrderService, ClaimsPrincipal User) dependency)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("At least one order item is required.");
        }

        var items = request.Items
            .Select(i => new CatalogItemQuantity(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await dependency.OrderService.CreateOrderFromItemsAsync(buyerId, items);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

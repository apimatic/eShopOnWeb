using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(request, http, service, http.RequestAborted))
            .Produces<PlaceOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        PlaceOrderRequest request, HttpContext http, IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var order = await service.PlaceOrderAsync(buyerId, lines, ct);
            return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse { OrderId = order.Id });
        }
        catch (OrderValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

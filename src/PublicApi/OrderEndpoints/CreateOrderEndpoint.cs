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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's Order/OrderItem model.
/// The shopper is told (by SMS, best-effort) that the order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var ownerId = user.ShopperId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                request.OwnerId = ownerId;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(CreateOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest("An order must contain at least one item.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(request.OwnerId, lines, cts.Token);
        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse { OrderId = order.Id });
    }
}

using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// Returns the new order's identifier as a top-level <c>orderId</c>.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<PlacedOrderResult>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(PlaceOrderRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineCommand(i.CatalogItemId, i.Quantity))
            .ToList();
        var result = await service.PlaceOrderAsync(request.BuyerId, lines, ToShippingCommand(request.ShipTo), ct);
        return result.ToHttpResult(placed => Results.Created($"api/orders/{placed.OrderId}", placed));
    }

    private static ShippingAddressCommand? ToShippingCommand(ShippingAddressDto? shipTo) =>
        shipTo is null ? null : new ShippingAddressCommand(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
}

using System.Linq;
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

public record CreateOrderCommand(string BuyerId, PlaceOrderRequest Body, CancellationToken Ct);

/// <summary>
/// POST /api/orders — a logged-in shopper places an order from catalog items. The order starts awaiting
/// payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new CreateOrderCommand(buyerId, request, http.RequestAborted), service);
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderCommand command, IPaymentApplicationService service)
    {
        var lines = (command.Body.Items ?? new())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();

        var shipTo = command.Body.ShipToAddress is { } a
            ? new ShippingAddressInput(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : null;

        var orderId = await service.PlaceOrderAsync(command.BuyerId, lines, shipTo, command.Ct);
        return Results.Created($"api/orders/{orderId}", new { orderId, status = "AwaitingPayment" });
    }
}

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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order reuses the app's
/// existing order/order-item model and starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Place an order awaiting payment", Tags = new[] { "Orders" })]
        async (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(request, user, service, ct))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        try
        {
            if (request?.Items is null || request.Items.Count == 0)
                return Results.BadRequest(new { message = "An order must contain at least one item." });

            var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
            var shipTo = request.ShipTo is null
                ? null
                : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

            var payment = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);

            var response = new PlaceOrderResponse(
                payment.OrderId,
                payment.Status.ToString(),
                payment.Amount,
                payment.CurrencyCode);

            return Results.Created($"api/orders/{payment.OrderId}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}

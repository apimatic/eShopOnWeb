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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — a signed-in shopper places an order from catalog items. The order starts
/// awaiting payment; prices come from the catalog, and the buyer is the token's identity.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderCommand, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new PlaceOrderCommand(PaymentUser.BuyerId(user), request, ct), service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PlaceOrderCommand command, IOrderPaymentService service)
    {
        var request = command.Request;
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("At least one order line is required.");
        }

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await service.PlaceOrderAsync(command.BuyerId, lines, address, command.Ct);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public record PlaceOrderCommand(string BuyerId, PlaceOrderRequest Request, CancellationToken Ct);

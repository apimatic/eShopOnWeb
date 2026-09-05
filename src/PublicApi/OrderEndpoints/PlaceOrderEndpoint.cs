using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts out awaiting payment; nothing is taken until
/// the shopper pays for it.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                request.Actor = RequestActor.From(caller);
                return await HandleAsync(request, payments);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();
        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            Currency = payments.Currency
        };

        var lines = request.Items
            .Select(line => new PlaceOrderLine(line.CatalogItemId, line.Quantity))
            .ToList();

        if (lines.Count == 0)
        {
            return Results.BadRequest(new { message = "An order needs at least one item." });
        }

        var shipTo = request.ShipTo is null
            ? new Address("not provided", "not provided", string.Empty, "US", "00000")
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
                request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await payments.PlaceOrderAsync(actor.BuyerId, lines, shipTo);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.OrderDate = order.OrderDate;
        response.Items = order.OrderItems
            .Select(item => new OrderLineDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
                LineTotal = item.UnitPrice * item.Units
            })
            .ToList();
        response.Note = $"The order is awaiting payment: POST api/orders/{order.Id}/pay.";

        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}

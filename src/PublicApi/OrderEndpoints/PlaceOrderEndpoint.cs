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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
        => HandleAsync(request, user, service, default);

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = user.BuyerId();

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = null;
        if (request.ShipToAddress is { } a)
        {
            shipTo = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);
        }

        var payment = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);

        var response = new PlaceOrderResponse
        {
            OrderId = payment.OrderId,
            Total = payment.Amount,
            Currency = payment.CurrencyCode,
            PaymentStatus = payment.Status.ToString(),
        };
        return Results.Created($"api/orders/{payment.OrderId}", response);
    }
}

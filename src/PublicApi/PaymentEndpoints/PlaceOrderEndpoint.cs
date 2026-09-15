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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. Reuses the app's existing Order/OrderItem model.
/// The order starts awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest>
{
    private readonly IPaymentService _payments;

    public PlaceOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request)
    {
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        var a = request.ShipToAddress;
        var address = a is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

        var order = await _payments.PlaceOrderAsync(request.BuyerId, lines, address);
        var payment = await _payments.GetOwnedPaymentAsync(order.Id, request.BuyerId);

        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse(order.Id, payment.ToDto()));
    }
}

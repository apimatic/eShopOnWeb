using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// POST /api/orders
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public PlaceOrderEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<PlaceOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var orderId = await _service.PlaceOrderAsync(buyerId, lines, request.ShipToAddress.ToDomainAddress());

        var response = new PlaceOrderResponse { OrderId = orderId, Status = PaymentStatus.AwaitingPayment.ToString() };
        return Results.Created($"api/orders/{orderId}", response);
    }
}

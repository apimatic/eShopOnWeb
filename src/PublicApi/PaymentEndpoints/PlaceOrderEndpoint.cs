using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PlaceOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service) => await HandleAsync(request, service))
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);

        var lines = (request.Items ?? new List<OrderLineApiModel>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipping = request.ShippingAddress is null
            ? null
            : new ShippingAddressInput(
                request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);

        var orderId = await service.PlaceOrderAsync(buyerId, lines, shipping, ctx.RequestAborted);
        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse(orderId, "AwaitingPayment"));
    }
}

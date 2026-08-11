using System.Linq;
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
/// Places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment; no money is taken here. Returns the new order id.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = CallerIdentity.BuyerId(_httpContextAccessor.HttpContext!);

        var lines = (request.Items ?? new())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = PaymentRequestMapper.ToAddress(request.ShipToAddress);
        var orderId = await paymentService.PlaceOrderAsync(buyerId, lines, address);

        return Results.Created($"api/orders/{orderId}",
            new PlaceOrderResponse(orderId, null));
    }
}

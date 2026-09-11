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
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderApiRequest, IPaymentService>
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
            (PlaceOrderApiRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PlaceOrderApiRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one order line is required." });
        }

        var orderId = await paymentService.PlaceOrderAsync(buyerId!, request.Items.ToOrderLines(), request.ShipToAddress.ToAddress());

        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = "AwaitingPayment"
        });
    }
}

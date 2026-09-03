using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PlaceOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IPaymentService service) => await HandleAsync(request, service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var orderId = await service.PlaceOrderAsync(
            ctx.User.BuyerId(), request.Items.ToLineInputs(), request.ShipToAddress.ToShippingInput(), ctx.RequestAborted);
        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
    }
}

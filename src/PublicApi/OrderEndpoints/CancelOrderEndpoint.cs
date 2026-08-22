using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutService checkout) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ICheckoutService checkout)
    {
        var payment = await checkout.CancelAsync(request.OrderId);
        return Results.Ok(new CancelOrderResponse
        {
            OrderId = payment.OrderId,
            Payment = OrderDtoMapper.MapPayment(payment)
        });
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

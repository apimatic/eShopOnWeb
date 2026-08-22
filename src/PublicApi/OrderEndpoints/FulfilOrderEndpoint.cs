using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutService checkout) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ICheckoutService checkout)
    {
        var payment = await checkout.FulfilAsync(request.OrderId);
        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = payment.OrderId,
            Payment = OrderDtoMapper.MapPayment(payment)
        });
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

using System.Security.Claims;
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
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the
/// money, renewing a stale authorization if needed. Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new OrderActionRequest { OrderId = orderId, Caller = CallerContext.From(user) },
                    paymentService);
            })
            .Produces<PaymentView>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilAsync(request.OrderId);
        return Results.Ok(payment);
    }
}

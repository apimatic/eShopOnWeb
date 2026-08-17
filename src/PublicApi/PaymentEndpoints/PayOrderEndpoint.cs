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
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total using a one-off card or one of
/// the shopper's saved cards. Does not capture. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.Caller = CallerContext.From(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PaymentView>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var instruction = new PayInstruction(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
        var payment = await paymentService.AuthorizeAsync(request.OrderId, request.Caller.Username, instruction);
        return Results.Ok(payment);
    }
}

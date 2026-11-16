using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refund a captured payment, in full or in part. Idempotency keys prevent a repeated
/// request from refunding twice while allowing distinct partial refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var (order, refund) = await paymentService.RefundOrderAsync(
            user.GetUserName(),
            request.OrderId,
            request.ToInput());

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Refund = OrderResponseMapper.ToDto(refund),
            Order = OrderResponseMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}

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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
                await HandleAsync(orderId, request, payments, user))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService payments)
        => HandleAsync(0, request, payments, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user)
    {
        var refund = await payments.RefundAsync(
            orderId,
            PaymentApiMapper.BuyerId(user),
            request.Amount,
            request.IdempotencyKey,
            PaymentApiMapper.IsAdministrator(user));
        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", PaymentApiMapper.FromRefund(refund));
    }
}

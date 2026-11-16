using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Return captured money after fulfilment, in full or in part. The caller supplies an
/// idempotency key so a repeated request does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundView>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var amountCents = request.AmountCents
                          ?? (request.Amount.HasValue ? Money.ToCents(request.Amount.Value) : (long?)null);

        var refund = await paymentService.RefundOrderAsync(
            request.BuyerId ?? string.Empty,
            request.OrderId,
            amountCents,
            request.IdempotencyKey);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", refund);
    }
}

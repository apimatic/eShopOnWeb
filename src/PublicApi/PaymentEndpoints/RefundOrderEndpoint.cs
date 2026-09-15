using System.Security.Claims;
using System.Threading;
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
/// Refunds a captured payment, in full or in part, against the caller's own order (an administrator may refund
/// any order). The caller-supplied idempotency key makes a repeated request harmless, while two distinct
/// partial refunds remain legitimate. Returns the new refund's identifier as a top-level <c>refundId</c>.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<RefundResultView>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(RefundOrderRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.RefundAsync(request.BuyerId, request.IsAdmin, request.OrderId, request.Amount, request.IdempotencyKey ?? string.Empty, ct);
        return result.ToHttpResult(refund => Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", refund));
    }
}

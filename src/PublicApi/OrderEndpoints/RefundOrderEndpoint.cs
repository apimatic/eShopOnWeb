using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record RefundOrderDependencies(int OrderId, string BuyerId, IOrderPaymentService Service);

/// <summary>Refunds a fulfilled order's captured payment, in full or in part.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, RefundOrderDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var deps = new RefundOrderDependencies(orderId, user.Identity!.Name!, orderPaymentService);
                return await HandleAsync(request, deps);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, RefundOrderDependencies deps)
    {
        if (string.IsNullOrEmpty(request.IdempotencyKey))
        {
            return Results.BadRequest("IdempotencyKey is required.");
        }

        var response = new RefundOrderResponse(request.CorrelationId());

        try
        {
            var result = await deps.Service.RefundAsync(deps.OrderId, deps.BuyerId, request.Amount, request.IdempotencyKey, default);
            if (result is null)
            {
                return Results.NotFound();
            }

            response.RefundId = result.Value.Refund.Id;
            response.PayPalRefundId = result.Value.Refund.PayPalRefundId;
            response.Status = result.Value.Refund.Status;
            response.Amount = result.Value.Refund.Amount;
            response.OrderId = result.Value.Order.Id;
            return Results.Ok(response);
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}

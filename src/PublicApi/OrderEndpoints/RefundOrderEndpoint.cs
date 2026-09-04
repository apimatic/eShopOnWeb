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
/// Refunds a captured payment, in full or in part. Repeating a request under the same
/// idempotency key returns the recorded refund instead of issuing another one.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public RefundOrderEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, request, user);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user)
    {
        var callerId = AuthenticatedUser.RequireIdentity(user);

        var command = new RefundCommand(request.Amount, request.IdempotencyKey);
        var result = await _payments.RefundAsync(orderId, callerId, AuthenticatedUser.IsAdmin(user), command);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.Refund.ProviderRefundId,
            OrderId = result.Order.Id,
            Amount = result.Refund.Amount,
            Currency = result.Refund.CurrencyCode,
            Status = result.Refund.Status,
            TotalRefundedAmount = result.Refund.TotalRefundedAmount,
            RemainingRefundableAmount = result.RemainingRefundableAmount,
            Replayed = result.Replayed
        };

        return result.Replayed
            ? Results.Ok(response)
            : Results.Created($"/api/my-orders", response);
    }
}

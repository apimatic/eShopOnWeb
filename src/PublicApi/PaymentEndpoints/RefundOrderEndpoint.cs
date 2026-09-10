using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Refunds a captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentValidationException("An idempotencyKey is required for a refund.");
        if (request.Amount is <= 0)
            throw new PaymentValidationException("Refund amount, when supplied, must be greater than zero.");

        var (payment, refund) = await service.RefundAsync(buyerId, request.OrderId, request.Amount,
            request.IdempotencyKey, CancellationToken.None);

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Refund = RefundResponse.From(refund),
            Payment = OrderPaymentResponse.From(payment),
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: gives back all or part of what was paid for an order, never more than was paid.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                // An empty body refunds everything that can still be refunded.
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.RequestedBy = OrderEndpointUser.RequireName(user);
                request.IdempotencyKey = idempotencyKey;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        // Not tied to the caller's connection: once the refund is sent, the outcome is always recorded.
        var result = await paymentService.RefundAsync(request.OrderId, request.Amount, request.Reason, request.RequestedBy,
            request.IdempotencyKey, CancellationToken.None);

        var order = result.Order;
        response.RefundId = result.Refund.Id;
        response.OrderId = order.Id;
        response.Refund = RefundDto.From(result.Refund);
        response.PaymentStatus = order.PaymentStatus().ToString();
        response.AmountRefundable = CurrencyMinorUnits.FromMinor(order.RefundableMinor(), result.Refund.Currency);

        return result.Replayed
            ? Results.Ok(response)
            : Results.Created($"api/orders/{order.Id}/refunds/{result.Refund.Id}", response);
    }
}

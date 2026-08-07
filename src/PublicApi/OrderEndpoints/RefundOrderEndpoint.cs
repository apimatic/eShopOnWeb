using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public OrderSummaryDto Order { get; set; } = new();
}

/// <summary>
/// Issues a full refund of an order's PayPal payment. Idempotent in effect: refunding an
/// already-refunded order returns its current state without refunding again.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(orderId, user, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IOrderPaymentService paymentService)
    {
        if (!user.TryGetBuyerId(out var buyerId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var order = await paymentService.RefundAsync(buyerId, orderId);

            var response = new RefundOrderResponse(Guid.NewGuid())
            {
                OrderId = order.Id,
                Order = PaymentApiMappings.ToSummary(order)
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (ex.IsHandledPaymentException())
        {
            return ex.ToProblemResult();
        }
    }
}

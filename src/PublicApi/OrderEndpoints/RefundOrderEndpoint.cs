using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>, IRepository<PaymentReference>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
             AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo,
             IRepository<PaymentReference> paymentRepo, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, orderRepo, paymentRepo, paymentService, orderId);
            })
            .Produces<RefundOrderResponse>()
            .WithName("RefundOrder")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo,
        IRepository<PaymentReference> paymentRepo, IPaymentService paymentService, int orderId)
    {
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order == null)
            return Results.NotFound("Order not found");

        var paymentRef = (await paymentRepo.ListAsync(p => p.OrderId == orderId)).FirstOrDefault();
        if (paymentRef == null)
            return Results.BadRequest("No payment reference found for order");

        if (paymentRef.State != PaymentState.Captured)
            return Results.BadRequest("Order is not in captured state");

        var refundAmount = request.Amount ?? paymentRef.CapturedAmount ?? 0m;
        var totalPotentialRefund = (paymentRef.CapturedAmount ?? 0m) - paymentRef.RefundedAmount;

        if (refundAmount > totalPotentialRefund)
            return Results.BadRequest($"Refund amount exceeds available amount. Available: {totalPotentialRefund}");

        var result = await paymentService.RefundPaymentAsync(paymentRef, refundAmount, request.IdempotencyKey);
        if (!result.Success)
            return Results.BadRequest(new { error = result.ErrorMessage });

        paymentRef.AddRefund(result.RefundId!, refundAmount);
        await paymentRepo.SaveChangesAsync();

        return Results.Ok(new RefundOrderResponse { RefundId = result.RefundId });
    }
}

public record RefundOrderRequest(string IdempotencyKey, decimal? Amount);
public record RefundOrderResponse
{
    public string? RefundId { get; set; }
}

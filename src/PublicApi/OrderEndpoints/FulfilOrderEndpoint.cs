using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public string Status { get; set; } = "";
    public string CaptureId { get; set; } = "";
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, IRepository<Payment> paymentRepo,
                   IPayPalService paypal, HttpContext httpContext, CancellationToken ct) =>
            {
                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), ct);
                if (order == null) return Results.NotFound("Order not found.");
                if (order.Status != OrderStatus.Authorized)
                    return Results.BadRequest($"Order must be in Authorized status to fulfil. Current: {order.Status}.");

                var payment = order.Payment;
                if (payment == null) return Results.BadRequest("Order has no payment record.");

                // Check authorization staleness
                var (isStale, canReauth) = await paypal.CheckAuthorizationAsync(payment.AuthorizationId, ct);
                if (isStale)
                {
                    if (!canReauth)
                        return Results.BadRequest("Authorization has expired beyond the renewal window (29 days). The order must be cancelled and restarted.");

                    var newAuthId = await paypal.ReauthorizeAsync(payment.AuthorizationId,
                        order.Total(), payment.Currency, ct);
                    payment.Reauthorize(newAuthId);
                    await paymentRepo.UpdateAsync(payment, ct);
                }

                var idempotencyKey = $"capture-{order.Id}";
                var result = await paypal.CaptureAsync(payment.AuthorizationId, idempotencyKey, ct);

                payment.SetCaptured(result.CaptureId, result.CapturedAmount, result.PayPalFee, result.NetAmount);
                await paymentRepo.UpdateAsync(payment, ct);
                order.SetFulfilled();
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new FulfilOrderResponse(Guid.NewGuid())
                {
                    Status = order.Status.ToString(),
                    CaptureId = result.CaptureId,
                    CapturedAmount = result.CapturedAmount,
                    PayPalFee = result.PayPalFee,
                    NetAmount = result.NetAmount
                });
            })
            .Produces<FulfilOrderResponse>()
            .Produces(400).Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}

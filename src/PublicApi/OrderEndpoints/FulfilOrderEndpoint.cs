using System;
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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IRepository<PaymentRecord>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPal,
                   CancellationToken ct) =>
            {
                var paySpec = new PaymentRecordByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(paySpec, ct);
                if (payment == null)
                    return Results.NotFound(new { error = "Payment record not found." });

                if (payment.Status != PaymentStatus.Authorized)
                    return Results.Conflict(new { error = $"Order cannot be fulfilled in state '{payment.Status}'." });

                if (string.IsNullOrEmpty(payment.AuthorizationId))
                    return Results.Conflict(new { error = "No authorization on record to capture." });

                var idempotencyKey = $"capture-order-{orderId}";

                PayPalCaptureResult captureResult;
                try
                {
                    captureResult = await payPal.CaptureAuthorizationAsync(payment.AuthorizationId, idempotencyKey, ct);
                }
                catch (PayPalAuthorizationRenewException ex)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Capture failed.");
                }

                if (captureResult.NewAuthorizationId != null)
                    payment.UpdateAuthorization(captureResult.NewAuthorizationId, "CREATED");

                payment.SetCaptured(
                    captureResult.CaptureId,
                    captureResult.CaptureStatus,
                    captureResult.CapturedAmount,
                    captureResult.Currency,
                    captureResult.PayPalFee,
                    captureResult.NetAmount);

                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new FulfilOrderResponse
                {
                    CaptureId = captureResult.CaptureId,
                    CapturedAmount = captureResult.CapturedAmount,
                    Currency = captureResult.Currency,
                    PayPalFee = captureResult.PayPalFee,
                    NetAmount = captureResult.NetAmount,
                    Status = payment.Status
                });
            })
            .Produces<FulfilOrderResponse>()
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<PaymentRecord> service)
        => throw new NotImplementedException();
}

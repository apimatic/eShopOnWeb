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

public class CancelOrderEndpoint : IEndpoint<IResult, int, IRepository<PaymentRecord>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
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
                    return Results.Conflict(new { error = $"Order cannot be cancelled in state '{payment.Status}'. Only Authorized orders can be cancelled." });

                if (string.IsNullOrEmpty(payment.AuthorizationId))
                    return Results.Conflict(new { error = "No authorization on record to void." });

                var idempotencyKey = $"void-order-{orderId}";
                try
                {
                    await payPal.VoidAuthorizationAsync(payment.AuthorizationId, idempotencyKey, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode ?? 502, title: "Cancellation failed.");
                }

                payment.SetVoided();
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new { status = payment.Status });
            })
            .Produces(200)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<PaymentRecord> service)
        => throw new NotImplementedException();
}

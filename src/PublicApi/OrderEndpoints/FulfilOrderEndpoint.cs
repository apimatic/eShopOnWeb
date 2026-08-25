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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Operator: captures payment and marks order fulfilled.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxReauthAge = TimeSpan.FromDays(29);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                IRepository<Order> orderRepo,
                IRepository<OrderPayment> paymentRepo,
                IPayPalPaymentService payPalService,
                PayPalSettings settings,
                CancellationToken ct) =>
            {
                var order = await orderRepo.GetBySpecAsync(new OrderByIdWithPaymentSpec(orderId), ct);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                var payment = order.Payment;
                if (payment == null || payment.PaymentStatus != PaymentStatuses.Authorized)
                    return Results.Conflict(new { error = "Order is not in an authorized state." });

                var authId = payment.AuthorizationId!;
                var currency = settings.Currency;
                var now = DateTimeOffset.UtcNow;

                // Stale-auth logic: determine if reauth is needed
                if (payment.AuthorizationExpiresAt.HasValue)
                {
                    var expiresAt = payment.AuthorizationExpiresAt.Value;
                    var authCreatedAt = expiresAt.AddDays(-29);
                    var ageFromCreation = now - authCreatedAt;

                    if (ageFromCreation > MaxReauthAge)
                        return Results.UnprocessableEntity(new { error = "Authorization expired and cannot be renewed; a new payment must be authorized." });

                    if (now > expiresAt - HonorPeriod)
                    {
                        // Outside 3-day honor period: reauthorize
                        string newAuthId;
                        try
                        {
                            newAuthId = await payPalService.ReauthorizeAsync(authId, order.Total(), currency, ct);
                        }
                        catch (PayPalException ex) when (ex.IsClientError)
                        {
                            return Results.UnprocessableEntity(new { error = $"Reauthorization failed: {ex.Message}" });
                        }
                        catch (PayPalException ex)
                        {
                            return Results.Problem($"Reauthorization failed: {ex.Message}", statusCode: 502);
                        }
                        payment.SetReauthorized(newAuthId, now.AddDays(29));
                        authId = newAuthId;
                    }
                }

                CaptureResult captureResult;
                try
                {
                    captureResult = await payPalService.CaptureAsync(authId, $"capture-{orderId}", ct);
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                payment.SetCaptured(captureResult.CaptureId, captureResult.GrossAmount, captureResult.PayPalFee, captureResult.NetAmount);
                await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new FulfilOrderResponse(
                    orderId,
                    captureResult.CaptureId,
                    captureResult.GrossAmount,
                    captureResult.PayPalFee,
                    captureResult.NetAmount));
            })
            .Produces<FulfilOrderResponse>()
            .Produces(404)
            .Produces(409)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}

public record FulfilOrderResponse(int OrderId, string? CaptureId, decimal CapturedAmount, decimal? PayPalFee, decimal? NetAmount);

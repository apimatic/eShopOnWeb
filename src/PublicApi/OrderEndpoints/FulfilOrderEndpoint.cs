using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse
{
    public string CaptureId { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class FulfilOrderEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx, orderId);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, 0);

    private async Task<IResult> HandleAsync(EmptyRequest _, HttpContext ctx, int orderId)
    {
        var sp = ctx.RequestServices;
        var paymentRepo = sp.GetRequiredService<IRepository<Payment>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        var paymentSpec = new PaymentByOrderIdSpec(orderId);
        var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec, ct);
        if (payment is null) return Results.NotFound("Payment record not found.");
        if (payment.Status != PaymentStatus.Authorized)
            return Results.BadRequest($"Order must be Authorized before fulfilment. Current status: {payment.Status}");

        var authId = payment.PayPalAuthorizationId!;
        var idempotencyKey = $"capture-{payment.Id}-{Guid.NewGuid():N}";

        // Handle stale authorization
        if (payment.AuthorizationExpiry.HasValue && payment.AuthorizationExpiry.Value <= DateTimeOffset.UtcNow)
        {
            try
            {
                var reauth = await paypalService.ReauthorizeAsync(
                    authorizationId: authId,
                    amount: payment.Amount,
                    currency: payment.Currency,
                    idempotencyKey: $"reauth-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                    ct: ct);

                payment.UpdateAuthorizationId(reauth.NewAuthorizationId, reauth.NewExpiry);
                authId = reauth.NewAuthorizationId;
                await paymentRepo.UpdateAsync(payment, ct);
            }
            catch (PayPalException ex)
            {
                return Results.Problem(
                    $"Authorization expired and re-authorization failed — order cannot be fulfilled. {ex.Message}",
                    statusCode: 422);
            }
        }

        CaptureResult captureResult;
        try
        {
            captureResult = await paypalService.CaptureAsync(authId, idempotencyKey, ct);
        }
        catch (PayPalException ex) when (ex.StatusCode == 409)
        {
            // May be stale — try to reauthorize and retry once
            try
            {
                var reauth = await paypalService.ReauthorizeAsync(
                    authorizationId: authId,
                    amount: payment.Amount,
                    currency: payment.Currency,
                    idempotencyKey: $"reauth-{orderId}-retry-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                    ct: ct);

                payment.UpdateAuthorizationId(reauth.NewAuthorizationId, reauth.NewExpiry);
                authId = reauth.NewAuthorizationId;
                await paymentRepo.UpdateAsync(payment, ct);

                captureResult = await paypalService.CaptureAsync(authId, idempotencyKey + "-retry", ct);
            }
            catch (PayPalException reEx)
            {
                return Results.Problem(
                    $"Authorization expired and re-authorization failed — order cannot be fulfilled. {reEx.Message}",
                    statusCode: 422);
            }
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        payment.RecordCapture(captureResult.CaptureId, captureResult.CapturedAmount, captureResult.PayPalFee, captureResult.NetAmount);
        await paymentRepo.UpdateAsync(payment, ct);

        return Results.Ok(new FulfilOrderResponse
        {
            CaptureId = captureResult.CaptureId,
            CapturedAmount = captureResult.CapturedAmount,
            PayPalFee = captureResult.PayPalFee,
            NetAmount = captureResult.NetAmount,
            PaymentStatus = payment.Status.ToString()
        });
    }
}

public class EmptyRequest { }

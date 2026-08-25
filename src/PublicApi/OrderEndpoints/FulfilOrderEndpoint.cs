using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalService paypal) =>
            {
                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null) return Results.NotFound($"Order {orderId} not found.");

                var payment = await paymentRepo.FirstOrDefaultAsync(
                    new OrderPaymentByOrderIdSpec(orderId));

                if (payment == null)
                    return Results.Problem("No payment record found for this order.", statusCode: 404);

                // Idempotency: already captured
                if (payment.Status == PaymentStatus.Captured)
                    return Results.Ok(new FulfilOrderResponse
                    {
                        OrderId = orderId,
                        CaptureId = payment.PayPalCaptureId!,
                        CapturedAmount = payment.CapturedAmount!.Value,
                        PayPalFee = payment.PayPalFee!.Value,
                        NetAmount = payment.NetAmount!.Value,
                        Status = payment.Status.ToString()
                    });

                if (payment.Status != PaymentStatus.Authorized)
                    return Results.Problem(
                        $"Order is in state '{payment.Status}' and cannot be fulfilled.",
                        statusCode: 409);

                var idempotencyKey = Guid.NewGuid().ToString("N");
                var authId = payment.PayPalAuthorizationId!;

                try
                {
                    // Check if authorization is expired; proactively reauthorize
                    var (authStatus, expiresAt) = await paypal.GetAuthorizationAsync(authId);
                    if (authStatus != "CREATED" || (expiresAt.HasValue && expiresAt.Value < DateTimeOffset.UtcNow))
                    {
                        try
                        {
                            var newAuthId = await paypal.ReauthorizeAsync(authId);
                            payment.UpdateAuthorizationId(newAuthId);
                            authId = newAuthId;
                        }
                        catch (PayPalException reAuthEx)
                        {
                            return Results.Problem(
                                $"Authorization has expired and cannot be renewed: {reAuthEx.Message}. " +
                                "Ask the shopper to re-pay the order.",
                                statusCode: 422);
                        }
                    }

                    var captureResult = await paypal.CaptureAsync(authId, idempotencyKey);
                    payment.SetCaptured(
                        captureResult.CaptureId,
                        captureResult.CapturedAmount,
                        captureResult.PayPalFee,
                        captureResult.NetAmount);
                    await paymentRepo.UpdateAsync(payment);

                    return Results.Ok(new FulfilOrderResponse
                    {
                        OrderId = orderId,
                        CaptureId = captureResult.CaptureId,
                        CapturedAmount = captureResult.CapturedAmount,
                        PayPalFee = captureResult.PayPalFee,
                        NetAmount = captureResult.NetAmount,
                        Status = payment.Status.ToString()
                    });
                }
                catch (PayPalException ex) when (ex.IsAuthorizationExpired)
                {
                    // Fallback: try reauthorize if the capture itself signalled expiry
                    try
                    {
                        var newAuthId = await paypal.ReauthorizeAsync(authId);
                        payment.UpdateAuthorizationId(newAuthId);
                        authId = newAuthId;

                        var captureResult = await paypal.CaptureAsync(authId, idempotencyKey + "-r");
                        payment.SetCaptured(
                            captureResult.CaptureId,
                            captureResult.CapturedAmount,
                            captureResult.PayPalFee,
                            captureResult.NetAmount);
                        await paymentRepo.UpdateAsync(payment);

                        return Results.Ok(new FulfilOrderResponse
                        {
                            OrderId = orderId,
                            CaptureId = captureResult.CaptureId,
                            CapturedAmount = captureResult.CapturedAmount,
                            PayPalFee = captureResult.PayPalFee,
                            NetAmount = captureResult.NetAmount,
                            Status = payment.Status.ToString()
                        });
                    }
                    catch (PayPalException reAuthEx)
                    {
                        return Results.Problem(
                            $"Authorization expired and reauthorization failed: {reAuthEx.Message}. " +
                            "Ask the shopper to re-pay the order.",
                            statusCode: 422);
                    }
                }
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IPayPalService dependency)
        => throw new NotImplementedException();
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = "";
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = "";
}

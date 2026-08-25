using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<OrderPayment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalClient paypal,
                   ILogger<FulfilOrderEndpoint> logger) =>
            {
                var spec = new OrderPaymentByOrderIdSpec(orderId);
                var payment = await paymentRepo.FirstOrDefaultAsync(spec);
                if (payment == null)
                    return Results.NotFound(new { error = "Order not found." });

                if (payment.Status == PaymentStatus.Captured)
                    return Results.Ok(new FulfilOrderResponse(
                        payment.Status.ToString(), payment.CaptureId!,
                        payment.CapturedAmount, payment.PayPalFee, payment.NetAmount));

                if (payment.Status != PaymentStatus.Authorized)
                    return Results.UnprocessableEntity(new { error = $"Order is in state {payment.Status}; only Authorized orders can be fulfilled." });

                var captureKey = $"eshop-capture-p{payment.Id}";

                try
                {
                    // Attempt capture
                    var capture = await paypal.CaptureAuthorizationAsync(payment.AuthorizationId!, captureKey);
                    return await ApplyCaptureAsync(payment, capture, paymentRepo);
                }
                catch (PayPalException captureEx)
                {
                    logger.LogWarning(captureEx, "Capture failed for order {OrderId}, checking auth status", orderId);

                    // Check if authorization is expired
                    PayPalGetAuthorizationResponse? authStatus = null;
                    try { authStatus = await paypal.GetAuthorizationAsync(payment.AuthorizationId!); }
                    catch (PayPalException ex2) { logger.LogWarning(ex2, "Could not get auth status"); }

                    if (authStatus?.Status == "EXPIRED")
                    {
                        // Try reauthorize
                        var reauthorizeKey = $"eshop-reauth-p{payment.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                        try
                        {
                            var reauth = await paypal.ReauthorizeAsync(
                                payment.AuthorizationId!,
                                payment.Amount.ToString("F2"),
                                payment.Currency,
                                reauthorizeKey);

                            if (string.IsNullOrEmpty(reauth.Id))
                                return Results.UnprocessableEntity(new { error = "Reauthorization failed: empty ID returned." });

                            payment.UpdateAuthorizationId(reauth.Id);
                            await paymentRepo.UpdateAsync(payment);

                            // Capture the new authorization
                            var newCaptureKey = $"eshop-capture-p{payment.Id}-reauth";
                            var capture2 = await paypal.CaptureAuthorizationAsync(reauth.Id, newCaptureKey);
                            return await ApplyCaptureAsync(payment, capture2, paymentRepo);
                        }
                        catch (PayPalException reauthEx)
                        {
                            logger.LogError(reauthEx, "Reauthorization failed for order {OrderId}", orderId);
                            return Results.UnprocessableEntity(new
                            {
                                error = "Authorization has expired and could not be renewed. " +
                                        "A new payment authorization is required from the customer before this order can be fulfilled.",
                                paypalDetail = reauthEx.Message
                            });
                        }
                    }

                    return Results.UnprocessableEntity(new { error = captureEx.Message, detail = captureEx.PayPalErrorBody });
                }
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> ApplyCaptureAsync(OrderPayment payment, PayPalCaptureResponse capture, IRepository<OrderPayment> repo)
    {
        if (!decimal.TryParse(capture.SellerReceivableBreakdown?.GrossAmount?.Value, out var gross))
            gross = payment.Amount;
        if (!decimal.TryParse(capture.SellerReceivableBreakdown?.PaypalFee?.Value, out var fee))
            fee = 0m;
        if (!decimal.TryParse(capture.SellerReceivableBreakdown?.NetAmount?.Value, out var net))
            net = gross - fee;

        payment.SetCaptured(capture.Id, gross, fee, net);
        await repo.UpdateAsync(payment);

        return Results.Ok(new FulfilOrderResponse(payment.Status.ToString(), capture.Id, gross, fee, net));
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<OrderPayment> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class FulfilOrderRequest : BaseRequest { }

public record FulfilOrderResponse(
    string Status,
    string CaptureId,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

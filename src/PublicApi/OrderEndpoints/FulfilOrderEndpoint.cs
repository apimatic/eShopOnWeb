using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   IPayPalService payPalService,
                   PayPalSettings settings) =>
            {
                var request = new FulfilOrderRequest { OrderId = orderId };
                return await HandleAsync(request, orderRepo, paymentRepo, payPalService, settings);
            })
            .Produces(200)
            .Produces(404)
            .Produces(409)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        FulfilOrderRequest request,
        IRepository<Order> orderRepo,
        IRepository<PaymentRecord> paymentRepo,
        IPayPalService payPalService,
        PayPalSettings settings)
    {
        var orderSpec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = (await orderRepo.ListAsync(orderSpec)).FirstOrDefault();
        if (order == null)
            return Results.NotFound(new { error = "Order not found." });

        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            return Results.Conflict(new { error = $"Order cannot be fulfilled in status '{order.PaymentStatus}'." });

        var paymentRecordSpec = new PaymentRecordByOrderIdSpec(request.OrderId);
        var paymentRecord = (await paymentRepo.ListAsync(paymentRecordSpec)).FirstOrDefault();
        if (paymentRecord?.AuthorizationId == null)
            return Results.Problem("Payment record missing authorization.", statusCode: 500);

        try
        {
            (bool isExpired, bool isVoidedOrDenied) = await payPalService.GetAuthorizationStatusAsync(
                paymentRecord.AuthorizationId);

            if (isVoidedOrDenied)
                return Results.UnprocessableEntity(new { error = "Authorization is voided or denied and cannot be captured." });

            string authId = paymentRecord.AuthorizationId;

            if (isExpired)
            {
                var newAuthId = await payPalService.ReauthorizeAsync(
                    authId, order.Total(), settings.Currency);
                paymentRecord.UpdateAuthorizationId(newAuthId);
                await paymentRepo.UpdateAsync(paymentRecord);
                authId = newAuthId;
            }

            var captureResult = await payPalService.CaptureAuthorizationAsync(
                authId, $"capture-{paymentRecord.IdempotencyBase}");

            paymentRecord.SetCaptured(captureResult.CaptureId, captureResult.GrossAmount,
                captureResult.FeeAmount, captureResult.NetAmount);
            await paymentRepo.UpdateAsync(paymentRecord);

            order.SetPaymentStatus(OrderPaymentStatus.Fulfilled);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new
            {
                captureId = captureResult.CaptureId,
                capturedAmount = captureResult.GrossAmount,
                paypalFee = captureResult.FeeAmount,
                netProceeds = captureResult.NetAmount,
                currency = settings.Currency
            });
        }
        catch (PayPalProviderException ex) when (ex.IsOperatorActionable)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (PayPalProviderException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem("PayPal returned an unreadable response.", statusCode: 502);
        }
    }
}

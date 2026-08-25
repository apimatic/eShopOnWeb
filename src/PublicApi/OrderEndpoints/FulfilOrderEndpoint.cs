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
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, IPayPalService paypal, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, orderRepo, paypal, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo,
        IPayPalService paypal, CancellationToken ct)
    {
        var spec = new OrderWithPaymentSpec(orderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);

        if (order is null) return Results.NotFound();
        if (order.Status != OrderStatus.PaymentAuthorized)
            return Results.BadRequest($"Order is in status '{order.Status}' and cannot be fulfilled.");
        if (order.Payment is null)
            return Results.BadRequest("Order has no payment information.");

        var authId = order.Payment.AuthorizationId;
        var idempotencyKey = $"fulfil-{orderId}";

        try
        {
            // Check if authorization has expired; renew if needed
            bool expired = await paypal.IsAuthorizationExpiredAsync(authId, ct);
            if (expired)
            {
                ReauthorizeResult reauth;
                try
                {
                    reauth = await paypal.ReauthorizeAsync(
                        authId, order.Total(), paypal.Currency, $"reauth-{orderId}", ct);
                }
                catch (PayPalException reauthEx) when (reauthEx.ErrorCode == "REAUTH_WINDOW_EXPIRED")
                {
                    return Results.Problem(
                        title: "Authorization cannot be renewed",
                        detail: "The payment authorization is older than 29 days and can no longer be renewed. Cancel the order and ask the customer to place a new one.",
                        statusCode: 422);
                }
                order.UpdateAuthorization(reauth.NewAuthorizationId, reauth.NewStatus, reauth.NewExpirationTime);
                await orderRepo.UpdateAsync(order, ct);
                authId = reauth.NewAuthorizationId;
            }

            var capture = await paypal.CaptureAuthorizationAsync(authId, idempotencyKey, ct);

            order.SetFulfilled(
                capture.CaptureId,
                capture.CapturedAmount,
                capture.PayPalFee,
                capture.NetAmount,
                capture.CaptureStatus);

            await orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = order.Id,
                CaptureId = capture.CaptureId,
                CapturedAmount = capture.CapturedAmount,
                PayPalFee = capture.PayPalFee,
                NetAmount = capture.NetAmount,
                CaptureStatus = capture.CaptureStatus
            });
        }
        catch (PayPalException ex) when (ex.ErrorCode == "AUTHORIZATION_EXPIRED")
        {
            return Results.Problem(
                title: "Authorization expired",
                detail: "The payment authorization expired. Cancel the order and ask the customer to place a new one.",
                statusCode: 422);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Fulfilment failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }
    }
}

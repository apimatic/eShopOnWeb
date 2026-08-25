using System;
using System.Security.Claims;
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

public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   IPayPalService payPal) =>
            {
                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId));
                if (order == null)
                    return Results.NotFound();

                if (order.Status == OrderStatus.Fulfilled)
                    return Results.Ok(OrderToDto(order));

                if (order.Status != OrderStatus.PaymentAuthorized)
                    return Results.BadRequest(new { error = $"Order is in status {order.Status} and cannot be fulfilled." });

                var authorizationId = order.AuthorizationId!;

                // Authorization is valid for 3 days; reauthorize between day 4 and day 29
                var daysSinceOrder = (DateTimeOffset.UtcNow - order.OrderDate).TotalDays;
                if (daysSinceOrder >= 3.0)
                {
                    if (daysSinceOrder >= 29.0)
                        return Results.BadRequest(new { error = "Authorization has expired (>29 days) and cannot be renewed. Please cancel and re-place the order." });

                    try
                    {
                        var newAuthId = await payPal.ReauthorizeAsync(authorizationId, order.Total(), "USD");
                        order.RenewAuthorization(newAuthId);
                        await orderRepo.UpdateAsync(order);
                        authorizationId = newAuthId;
                    }
                    catch (PayPalException ex)
                    {
                        return Results.BadRequest(new { error = $"Reauthorization failed: {ex.Message}. Please cancel and re-place the order." });
                    }
                }

                CaptureResult captureResult;
                try
                {
                    captureResult = await payPal.CaptureAsync(authorizationId);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = $"Capture failed: {ex.Message}" });
                }

                order.SetCaptured(
                    captureResult.CaptureId,
                    captureResult.CapturedAmount,
                    captureResult.PayPalFee,
                    captureResult.NetAmount);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(OrderToDto(order));
            })
            .WithTags("OrderEndpoints");
    }

    private static object OrderToDto(Order o) => new
    {
        orderId = o.Id,
        status = o.Status.ToString(),
        captureId = o.CaptureId,
        capturedAmount = o.CapturedAmount,
        payPalFee = o.PayPalFee,
        netAmount = o.NetAmount
    };
}

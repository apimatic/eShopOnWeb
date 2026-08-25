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
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse
{
    public string CaptureId { get; set; } = "";
    public decimal CapturedAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string PaymentStatus { get; set; } = "";
}

public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   PayPalClient paypal,
                   IOptions<PayPalSettings> settings) =>
            {
                var spec = new OrderWithPaymentSpec(orderId);
                var order = await orderRepo.GetBySpecAsync(spec);
                if (order == null)
                    return Results.NotFound(new { error = "Order not found." });

                if (order.PaymentStatus != PaymentStatus.Authorized)
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Order cannot be fulfilled in its current state: {order.PaymentStatus}."
                    });

                var currency = settings.Value.Currency;
                var amount = order.Total();
                var authId = order.PayPalAuthorizationId!;

                PayPalCaptureResult captureResult;
                try
                {
                    captureResult = await paypal.CaptureAuthorizationAsync(authId, amount, currency);
                }
                catch (PayPalException ex) when (ex.PayPalName == "AUTHORIZATION_EXPIRED" ||
                                                  ex.PayPalName == "AUTHORIZATION_VOIDED")
                {
                    // Authorization is stale — attempt reauthorization
                    PayPalReauthorizeResult reauth;
                    try
                    {
                        reauth = await paypal.ReauthorizeAsync(authId, amount, currency);
                    }
                    catch (PayPalException reEx)
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = $"Authorization has expired and could not be renewed: {reEx.Message}. " +
                                    "The authorization is older than 29 days or has been voided. " +
                                    "Cancel this order and ask the shopper to place a new one.",
                            paypalCode = reEx.PayPalName
                        });
                    }

                    // Update order with new auth ID before capturing
                    order.UpdateAuthorization(reauth.NewAuthorizationId, reauth.ExpiresAt);
                    await orderRepo.UpdateAsync(order);

                    try
                    {
                        captureResult = await paypal.CaptureAuthorizationAsync(
                            reauth.NewAuthorizationId, amount, currency);
                    }
                    catch (PayPalException capEx)
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = $"Reauthorization succeeded but capture failed: {capEx.Message}",
                            paypalCode = capEx.PayPalName
                        });
                    }
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Capture failed: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                // Fallback: sandbox may not always return amounts in the immediate capture response
                var captured = captureResult.GrossAmount > 0 ? captureResult.GrossAmount : amount;
                var fee = captureResult.FeeAmount;
                var net = captureResult.NetAmount > 0 ? captureResult.NetAmount : captured - fee;

                order.MarkFulfilled(captureResult.CaptureId, captured, fee);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new FulfilOrderResponse
                {
                    CaptureId = captureResult.CaptureId,
                    CapturedAmount = captured,
                    FeeAmount = fee,
                    NetAmount = net,
                    PaymentStatus = order.PaymentStatus.ToString()
                });
            })
            .Produces<FulfilOrderResponse>()
            .ProducesProblem(422)
            .WithTags("OrderEndpoints");
    }
}

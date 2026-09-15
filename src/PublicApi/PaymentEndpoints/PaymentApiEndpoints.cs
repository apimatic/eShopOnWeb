using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentApiEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
        };
        var administrator = new AuthorizeAttribute
        {
            AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
            Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
        };

        app.MapPost("api/orders", async (CreateOrderRequest request, HttpContext context,
                CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Created("api/orders", await service.CreateOrderAsync(
                BuyerId(context), request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay", async (int orderId, PayOrderRequest request,
                HttpContext context, CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PayAsync(BuyerId(context), orderId, request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/fulfil", async (int orderId,
                CommercePaymentService service, CancellationToken cancellationToken) =>
            {
                var response = await service.FulfilAsync(orderId, cancellationToken);
                return response.Order.Payment?.CaptureStatus == "PENDING"
                    ? Results.Accepted($"api/orders/{orderId}/fulfil", response)
                    : Results.Ok(response);
            })
            .RequireAuthorization(administrator)
            .Produces<FulfilOrderResponse>()
            .Produces<FulfilOrderResponse>(StatusCodes.Status202Accepted)
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel", async (int orderId,
                CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CancelAsync(orderId, cancellationToken)))
            .RequireAuthorization(administrator)
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/refunds", async (int orderId,
                RefundOrderRequest request, HttpContext context, CommercePaymentService service,
                CancellationToken cancellationToken) =>
            Results.Created($"api/orders/{orderId}/refunds",
                await service.RefundAsync(BuyerId(context), orderId, request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapGet("api/my-orders", async (HttpContext context, CommercePaymentService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMyOrdersAsync(BuyerId(context), cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");

        app.MapPost("api/payment-methods", async (SavePaymentMethodRequest request,
                HttpContext context, CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Created("api/payment-methods", await service.SavePaymentMethodAsync(
                BuyerId(context), request, cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");

        app.MapGet("api/payment-methods", async (HttpContext context,
                CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetPaymentMethodsAsync(BuyerId(context), cancellationToken)))
            .RequireAuthorization(shopper)
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId:int}", async (int paymentMethodId,
                HttpContext context, CommercePaymentService service, CancellationToken cancellationToken) =>
            {
                await service.DeletePaymentMethodAsync(BuyerId(context), paymentMethodId,
                    cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization(shopper)
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");

        app.MapGet("api/reconciliation", async (DateTimeOffset from, DateTimeOffset to,
                CommercePaymentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)))
            .RequireAuthorization(administrator)
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    private static string BuyerId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.Name)
        ?? throw new CommerceException(StatusCodes.Status401Unauthorized, "identity_missing",
            "The bearer token does not contain a shopper identity.");
}

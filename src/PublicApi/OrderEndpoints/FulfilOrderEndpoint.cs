using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IPayPalGateway _paypal;

    public FulfilOrderEndpoint(IRepository<Order> orderRepo, IPayPalGateway paypal)
    {
        _orderRepo = orderRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext ctx) =>
            {
                return await HandleAsync(orderId, ctx.RequestAborted);
            })
            .Produces<FulfilOrderResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(int orderId, System.Threading.CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return Results.NotFound();

        if (order.PaymentStatus == PaymentStatus.Fulfilled)
            return Results.Ok(new FulfilOrderResponse
            {
                CaptureId = order.PayPalCaptureId!,
                CapturedAmount = order.CapturedAmount ?? 0m,
                PayPalFee = order.PayPalFeeAmount,
                NetAmount = order.NetAmount,
                Status = order.PaymentStatus.ToString()
            });

        if (order.PaymentStatus != PaymentStatus.Authorized)
            return Results.Problem($"Order cannot be fulfilled in its current state: {order.PaymentStatus}", statusCode: 409);

        var authId = order.PayPalAuthorizationId!;

        // Re-authorize if stale
        if (order.AuthorizationExpiresAt.HasValue && order.AuthorizationExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            try
            {
                var reauth = await _paypal.ReauthorizeAsync(order.Id, authId, order.Total(), order.Currency ?? "USD", ct);
                order.UpdateAuthorization(reauth.NewAuthorizationId, reauth.NewExpiresAt);
                authId = reauth.NewAuthorizationId;
                await _orderRepo.UpdateAsync(order, ct);
            }
            catch (PayPalException ex) when (ex.Kind == PayPalErrorKind.ReauthorizationImpossible)
            {
                return Results.Problem(ex.Message, statusCode: 422);
            }
            catch (PayPalException ex)
            {
                return Results.Problem($"Re-authorization failed: {ex.Message}", statusCode: 502);
            }
        }

        try
        {
            var capture = await _paypal.CaptureAsync(order.Id, authId, ct);
            order.MarkFulfilled(capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new FulfilOrderResponse
            {
                CaptureId = capture.CaptureId,
                CapturedAmount = capture.CapturedAmount,
                PayPalFee = capture.PayPalFee,
                NetAmount = capture.NetAmount,
                Status = order.PaymentStatus.ToString()
            });
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"Capture failed: {ex.Message}", statusCode: 502);
        }
    }
}

public class FulfilOrderResponse
{
    public string CaptureId { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

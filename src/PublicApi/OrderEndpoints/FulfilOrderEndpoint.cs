using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = "";
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IRepository<OrderPayment> _paymentRepo;
    private readonly IPayPalClient _paypal;

    public FulfilOrderEndpoint(IRepository<OrderPayment> paymentRepo, IPayPalClient paypal)
    {
        _paymentRepo = paymentRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                       Roles = "Administrators")]
            async (int orderId, IRepository<Order> orderRepo, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderRepo, ct);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> repository)
        => HandleAsync(request, repository, default);

    private async Task<IResult> HandleAsync(FulfilOrderRequest request,
        IRepository<Order> orderRepo, CancellationToken ct)
    {
        var order = await orderRepo.GetByIdAsync(request.OrderId, ct);
        if (order == null)
            return Results.NotFound();

        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepo.FirstOrDefaultAsync(spec, ct);
        if (payment == null)
            return Results.Problem("Payment record not found.");

        // Idempotency: already captured
        if (payment.Status == OrderPaymentStatus.Captured)
            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString(),
                CaptureId = payment.CaptureId,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                Currency = payment.Currency
            });

        if (payment.Status != OrderPaymentStatus.Authorized)
            return Results.BadRequest(new { error = $"Order cannot be fulfilled in status '{payment.Status}'." });

        var authId = payment.AuthorizationId!;
        var now = DateTimeOffset.UtcNow;
        var authCreated = payment.AuthorizationCreatedAt ?? now;
        var authExpiry = payment.AuthorizationExpiry ?? now.AddDays(29);

        if (now > authExpiry)
        {
            return Results.UnprocessableEntity(new
            {
                error = "The PayPal authorization has expired and can no longer be renewed. " +
                        "Cancel this order and ask the customer to place a new one.",
                authorizationId = authId,
                expiredAt = authExpiry
            });
        }

        // If past the 3-day honor period, reauthorize before capturing
        var honorPeriodEnd = authCreated.AddDays(3);
        if (now > honorPeriodEnd)
        {
            try
            {
                var reauth = await _paypal.ReauthorizeAsync(authId, payment.Amount, payment.Currency, ct);
                payment.UpdateAuthorization(reauth.NewAuthorizationId, reauth.ExpirationTime);
                await _paymentRepo.UpdateAsync(payment, ct);
                authId = reauth.NewAuthorizationId;
            }
            catch (PayPalException ex)
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"Reauthorization failed: {ex.Message}. " +
                            "Cancel this order and ask the customer to place a new one.",
                    code = ex.PayPalErrorName
                });
            }
        }

        try
        {
            var idempotencyKey = payment.CaptureIdempotencyKey;
            var capture = await _paypal.CaptureAuthorizationAsync(authId, idempotencyKey, ct);

            payment.MarkCaptured(capture.CaptureId, capture.CapturedAmount,
                capture.PayPalFee, capture.NetAmount);
            await _paymentRepo.UpdateAsync(payment, ct);

            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString(),
                CaptureId = payment.CaptureId,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                Currency = payment.Currency
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }
    }
}

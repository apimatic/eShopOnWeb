using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly Infrastructure.PayPal.PayPalSettings _settings;

    public RefundOrderEndpoint(IPayPalPaymentService payPal, IOptions<Infrastructure.PayPal.PayPalSettings> settings)
    {
        _payPal = payPal;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderRepo);
            })
            .Produces<RefundOrderResponse>(201)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.IdempotencyKey))
            return Results.BadRequest("IdempotencyKey is required.");

        var spec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);

        if (order == null)
            return Results.NotFound();

        if (order.PaymentStatus != PaymentStatus.Fulfilled &&
            order.PaymentStatus != PaymentStatus.PartiallyRefunded)
            return Results.Conflict($"Order cannot be refunded in current state: {order.PaymentStatus}");

        // Idempotency: if same key already exists, return existing refund
        foreach (var existing in order.Refunds)
        {
            if (existing.IdempotencyKey == request.IdempotencyKey)
            {
                return Results.Ok(new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = existing.RefundId,
                    RefundedAmount = existing.Amount,
                    Status = order.PaymentStatus.ToString()
                });
            }
        }

        // Guard: partial refund cannot exceed remaining refundable amount
        var capturedAmount = order.CapturedAmount ?? 0m;
        var alreadyRefunded = order.TotalRefunded();
        var refundable = capturedAmount - alreadyRefunded;

        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
                return Results.BadRequest("Refund amount must be positive.");
            if (request.Amount.Value > refundable)
                return Results.UnprocessableEntity(new
                {
                    error = $"Refund amount {request.Amount.Value:F2} exceeds refundable amount {refundable:F2}."
                });
        }

        try
        {
            var result = await _payPal.RefundAsync(
                captureId: order.CaptureId!,
                partialAmount: request.Amount,
                currency: _settings.Currency,
                idempotencyKey: request.IdempotencyKey,
                capturedAmount: refundable);

            order.RecordRefund(result.RefundId, request.IdempotencyKey, result.Amount);
            await orderRepo.UpdateAsync(order);

            return Results.Created($"/api/orders/{request.OrderId}/refunds/{result.RefundId}",
                new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = result.RefundId,
                    RefundedAmount = result.Amount,
                    Status = order.PaymentStatus.ToString()
                });
        }
        catch (PayPalOperationException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Refund error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public string RefundId { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

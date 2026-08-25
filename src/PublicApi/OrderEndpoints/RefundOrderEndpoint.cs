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
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPayPalPaymentService>
{
    private readonly IRepository<OrderPayment> _paymentRepository;

    public RefundOrderEndpoint(IRepository<OrderPayment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Administrators")]
            async (int orderId, RefundOrderRequestBody body, IPayPalPaymentService paymentService) =>
            {
                var request = new RefundOrderRequest
                {
                    OrderId = orderId,
                    IdempotencyKey = body.IdempotencyKey ?? string.Empty,
                    Amount = body.Amount
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(200)
            .Produces(400)
            .Produces(404)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPayPalPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.IdempotencyKey))
            return Results.BadRequest(new { error = "idempotencyKey is required." });

        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(spec);

        if (payment is null) return Results.NotFound(new { error = "Order payment not found." });

        if (payment.Status is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
            return Results.UnprocessableEntity(new { error = $"Cannot refund an order in state: {payment.Status}." });

        // Idempotency: already refunded with this key?
        foreach (var r in payment.Refunds)
        {
            if (r.IdempotencyKey == request.IdempotencyKey)
                return Results.Ok(new { refundId = r.RefundId });
        }

        // Over-refund guard
        var refundAmount = request.Amount ?? payment.CapturedAmount ?? 0m;
        if (refundAmount <= 0)
            return Results.BadRequest(new { error = "Refund amount must be positive." });

        var alreadyRefunded = payment.TotalRefunded();
        var capturedAmount = payment.CapturedAmount ?? 0m;
        if (alreadyRefunded + refundAmount > capturedAmount)
            return Results.UnprocessableEntity(new
            {
                error = $"Refund of {refundAmount} would exceed captured amount of {capturedAmount} (already refunded: {alreadyRefunded})."
            });

        RefundResult result;
        try
        {
            result = await paymentService.RefundAsync(payment, request.IdempotencyKey, request.Amount, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        var refund = new OrderRefund(payment.Id, result.RefundId, request.IdempotencyKey, result.Amount, payment.Currency, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(new { refundId = result.RefundId });
    }
}

public class RefundOrderRequestBody
{
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
}

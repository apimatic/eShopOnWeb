using System.Linq;
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
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record RefundOrderRequest(int OrderId, string IdempotencyKey, decimal? Amount = null);
public record RefundOrderResponse(string RefundId, string Status, string? Amount);

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IPayPalPaymentService payPal, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _payPal = payPal;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IRepository<Order> orderRepo) =>
            {
                var mergedRequest = request with { OrderId = orderId };
                return await HandleAsync(mergedRequest, orderRepo);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("IdempotencyKey is required.");

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var spec = new OrderWithPaymentByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
        if (order == null) return Results.NotFound();

        if (order.PaymentStatus != PaymentStatus.Fulfilled && order.PaymentStatus != PaymentStatus.Refunded)
            return Results.BadRequest($"Order is in state {order.PaymentStatus} and cannot be refunded.");

        if (string.IsNullOrEmpty(order.CaptureId))
            return Results.BadRequest("No capture ID on this order.");

        var existingRefund = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existingRefund != null)
            return Results.Ok(new RefundOrderResponse(existingRefund.PayPalRefundId, "COMPLETED", existingRefund.Amount.ToString("F2")));

        var currency = _config["PayPal:Currency"] ?? "USD";

        try
        {
            var result = await _payPal.RefundCaptureAsync(order.CaptureId, request.Amount, currency, request.IdempotencyKey, ct);

            var refundAmount = request.Amount ?? order.Total();
            var refund = new OrderRefund(order.Id, result.RefundId, refundAmount, request.IdempotencyKey);
            order.AddRefund(refund);
            await orderRepo.UpdateAsync(order, ct);

            return Results.Created($"api/orders/{order.Id}/refunds/{result.RefundId}",
                new RefundOrderResponse(result.RefundId, result.Status ?? "", result.Amount));
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}

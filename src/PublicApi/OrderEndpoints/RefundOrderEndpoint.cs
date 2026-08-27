using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part. Shoppers refund only their own
/// orders; administrators can refund any order. Idempotent per idempotency key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IRepository<ApplicationCore.Entities.PaymentAggregate.Payment> _paymentRepository;

    public RefundOrderEndpoint(IRepository<ApplicationCore.Entities.PaymentAggregate.Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                request.IsOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var refund = await paymentService.RefundOrderAsync(
            request.BuyerId, request.IsOperator, request.OrderId,
            request.Amount, request.IdempotencyKey, request.NoteToPayer);
        if (refund is null)
            return Results.NotFound();

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new ApplicationCore.Specifications.PaymentByOrderIdSpecification(request.OrderId));

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            TotalRefunded = payment?.TotalRefunded ?? refund.Amount,
            RemainingRefundableAmount = payment?.RefundableAmount ?? 0m
        });
    }
}

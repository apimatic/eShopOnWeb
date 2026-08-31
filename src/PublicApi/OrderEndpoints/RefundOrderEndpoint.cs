using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds the captured payment, in full or in part, after fulfilment.
/// The idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, int>
{
    private readonly IPaymentService _paymentService;
    private readonly IRepository<Payment> _paymentRepository;

    public RefundOrderEndpoint(IPaymentService paymentService, IRepository<Payment> paymentRepository)
    {
        _paymentService = paymentService;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request) =>
            {
                return await HandleAsync(request, orderId);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, int orderId)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentDomainException("An idempotencyKey is required to issue a refund.");
        }

        var refund = await _paymentService.RefundAsync(orderId, request.Amount, request.NoteToPayer, request.IdempotencyKey);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = orderId,
            PaymentId = payment!.Id,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = payment.Currency,
            TotalRefunded = payment.TotalRefunded,
            RemainingRefundable = payment.RefundableAmount
        };
        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}

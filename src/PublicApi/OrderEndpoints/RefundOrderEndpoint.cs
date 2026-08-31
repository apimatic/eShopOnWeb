using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds the captured payment of a fulfilled order, in full or in part.
/// Repeating a request under the same idempotency key returns the original
/// refund instead of refunding twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ICurrentUser _currentUser;
    private readonly IReadRepository<ApplicationCore.Entities.PaymentAggregate.Payment> _paymentRepository;
    private readonly PayPalSettings _payPalSettings;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService,
        ICurrentUser currentUser,
        IReadRepository<ApplicationCore.Entities.PaymentAggregate.Payment> paymentRepository,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderPaymentService = orderPaymentService;
        _currentUser = currentUser;
        _paymentRepository = paymentRepository;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await _orderPaymentService.RefundOrderAsync(
            request.OrderId,
            _currentUser.BuyerId,
            request.Amount,
            request.IdempotencyKey,
            request.NoteToPayer,
            _payPalSettings.Currency);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));

        response.RefundId = refund.PayPalRefundId;
        response.OrderId = request.OrderId;
        response.Amount = refund.Amount;
        response.Status = refund.Status;
        response.Currency = payment?.Currency ?? _payPalSettings.Currency;
        response.TotalRefunded = payment?.TotalRefunded ?? refund.Amount;
        response.RemainingRefundable = payment?.RemainingRefundable ?? 0m;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Populated from the route.</summary>
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating the request under the same key
    /// returns the original refund; a distinct key performs a distinct refund.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) {}
    public RefundOrderResponse() {}

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}

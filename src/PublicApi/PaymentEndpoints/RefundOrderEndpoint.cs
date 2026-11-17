using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.RefundOrderRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, RefundOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        var amount = request.Amount;
        if (!amount.HasValue && request.AmountInCents.HasValue)
        {
            amount = request.AmountInCents.Value / 100m;
        }

        var result = await _paymentService.RefundAsync(
            new RefundOrderCommand(request.OrderId, buyerId, amount, request.IdempotencyKey ?? string.Empty),
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Created($"api/orders/{result.OrderId}/refunds/{result.RefundId}", new RefundOrderResponse
        {
            RefundId = result.RefundId,
            OrderId = result.OrderId,
            Amount = result.Amount,
            AmountCents = result.AmountCents,
            Currency = result.Currency,
            Status = result.Status,
            IdempotencyKey = result.IdempotencyKey,
            FinixTransferId = result.FinixTransferId,
            IdempotentReplay = result.IdempotentReplay
        });
    }

    public class RefundOrderRequest
    {
        [JsonIgnore]
        public int OrderId { get; set; }
        public decimal? Amount { get; set; }
        public long? AmountInCents { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class RefundOrderResponse
    {
        public int RefundId { get; set; }
        public int OrderId { get; set; }
        public decimal Amount { get; set; }
        public long AmountCents { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string? FinixTransferId { get; set; }
        public bool IdempotentReplay { get; set; }
    }
}


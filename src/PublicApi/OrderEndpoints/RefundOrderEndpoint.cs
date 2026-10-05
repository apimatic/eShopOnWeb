using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-chosen key: repeating a request under it never refunds twice. May also be sent as the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    [JsonPropertyOrder(-1)]
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public RefundDto? Refund { get; set; }
    public OrderDto? Order { get; set; }
}

/// <summary>Refunds the shopper's captured payment, in full or in part, never beyond what was captured.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentService>
{
    private readonly PaymentSettings _paymentSettings;

    public RefundOrderEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefundOrderRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
                ClaimsPrincipal user, PaymentService paymentService, CancellationToken ct) =>
            {
                request ??= new RefundOrderRequest();
                if (!string.IsNullOrWhiteSpace(idempotencyKeyHeader) && !string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && idempotencyKeyHeader != request.IdempotencyKey)
                {
                    throw new PaymentRequestException(PaymentErrorKind.Validation, "idempotency_key_mismatch",
                        "The Idempotency-Key header and the idempotencyKey field differ.");
                }
                request.IdempotencyKey ??= idempotencyKeyHeader;
                request.OrderId = orderId;
                request.BuyerId = PaymentEndpointUser.BuyerId(user);
                request.RequestAborted = ct;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentService paymentService)
    {
        var (payment, refund) = await paymentService.RefundAsync(request.OrderId, request.BuyerId, request.Amount,
            request.IdempotencyKey ?? string.Empty, request.RequestAborted);
        var order = await OrderResults.BuildAsync(request.CorrelationId(), request.OrderId, paymentService, _paymentSettings, request.RequestAborted);
        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            OrderId = payment.OrderId,
            Refund = order.Order?.Payment?.Refunds.FirstOrDefault(r => r.RefundId == refund.Id) ?? RefundDto.From(refund),
            Order = order.Order
        });
    }
}

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, PaymentService>
{
    private readonly PaymentSettings _paymentSettings;

    public MyOrdersEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentService paymentService, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = PaymentEndpointUser.BuyerId(user), RequestAborted = ct }, paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, PaymentService paymentService)
    {
        var orders = await paymentService.ListOrdersForBuyerAsync(request.BuyerId, request.RequestAborted);
        return Results.Ok(new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => OrderDto.From(o.Order, o.Payment, _paymentSettings.Currency)).ToList()
        });
    }
}

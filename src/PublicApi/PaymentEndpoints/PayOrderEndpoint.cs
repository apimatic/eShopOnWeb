using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Target order id, taken from the route (not the body).</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>One-off card to pay with. Provide this OR <see cref="PaymentMethodId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>A saved card (payment method) id to pay with instead of raw card details.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. Shopper-scoped and idempotent: a
/// double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var instrument = new PaymentInstrument(request.Card?.ToCardDetails(), request.PaymentMethodId);

        var payment = await paymentService.AuthorizeAsync(request.OrderId, buyerId, instrument, cancellationToken);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = PaymentMapping.ToDto(payment)
        });
    }
}

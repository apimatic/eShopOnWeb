using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details. Mutually exclusive with <see cref="PaymentMethodId"/>.</summary>
    public CardInputDto? Card { get; set; }

    /// <summary>One of the caller's saved cards. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }

    public override string ToString() => $"PayOrderRequest(order {OrderId}, {(PaymentMethodId is null ? "card" : "saved card")})";
}

/// <summary>
/// Authorizes (holds) the order total on the shopper's card or saved card. No money is taken until fulfilment.
/// Repeating the call never places a second hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PaymentService>
{
    private readonly PaymentSettings _paymentSettings;

    public PayOrderEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, PaymentService paymentService, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentEndpointUser.BuyerId(user);
                request.RequestAborted = ct;
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PaymentService paymentService)
    {
        var command = new PayOrderCommand(request.Card?.ToCardDetails(), request.PaymentMethodId);
        var payment = await paymentService.PayAsync(request.OrderId, request.BuyerId, command, request.RequestAborted);
        return Results.Ok(await OrderResults.BuildAsync(request.CorrelationId(), payment.OrderId, paymentService, _paymentSettings, request.RequestAborted));
    }
}

/// <summary>Builds the standard order + payment response after an action.</summary>
internal static class OrderResults
{
    public static async Task<OrderResponse> BuildAsync(System.Guid correlationId, int orderId, PaymentService paymentService,
        PaymentSettings settings, CancellationToken ct)
    {
        var (order, payment) = await paymentService.GetOrderWithPaymentAsync(orderId, ct);
        return new OrderResponse(correlationId)
        {
            OrderId = orderId,
            Order = order is null ? null : OrderDto.From(order, payment, settings.Currency)
        };
    }
}

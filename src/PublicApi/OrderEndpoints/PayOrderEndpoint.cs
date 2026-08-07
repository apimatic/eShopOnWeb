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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Pays for an order with PayPal, using either supplied card details or a saved card.
/// Idempotent: a repeated call never produces a second charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
        => HandleAsync(request, paymentService, default);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService, CancellationToken ct)
    {
        var hasCard = request.Card is not null && request.Card.HasCoreDetails;
        var hasSaved = request.SavedPaymentMethodId is > 0;

        if (hasCard == hasSaved)
        {
            return Results.BadRequest(new { message = "Provide either card details or a savedPaymentMethodId, but not both." });
        }

        Order order = hasSaved
            ? await paymentService.PayOrderWithSavedMethodAsync(request.OrderId, request.BuyerId, request.SavedPaymentMethodId!.Value, ct)
            : await paymentService.PayOrderWithCardAsync(request.OrderId, request.BuyerId, request.Card!.ToCardPaymentDetails(), ct);

        return Results.Ok(new PayOrderResponse(request.CorrelationId()) { Order = OrderDto.From(order) });
    }
}

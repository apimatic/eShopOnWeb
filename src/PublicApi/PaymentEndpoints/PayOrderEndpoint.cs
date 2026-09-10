using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card details for a one-off payment. Mutually exclusive with
    /// <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total against a card or a saved card. Does not take the
/// money — that happens at fulfilment. Idempotent: a double-click never holds twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.CallerId = PaymentEndpointHelpers.GetCallerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var hasCard = request.Card is not null;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide exactly one of 'card' or 'savedPaymentMethodId'.");
        }

        PaymentInstrument instrument = hasCard
            ? new CardPaymentInstrument(request.Card!.ToCardDetails())
            : new SavedCardPaymentInstrument(request.SavedPaymentMethodId!.Value);

        var order = await service.AuthorizeAsync(request.OrderId, request.CallerId, instrument);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}

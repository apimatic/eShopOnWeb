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
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this or <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of a one-off card.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total without taking the money, using a one-off or saved card.
/// Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        PayInstruction instruction;
        if (request.SavedPaymentMethodId is { } savedId)
        {
            instruction = new PayWithSavedCardInstruction(savedId);
        }
        else if (request.Card is not null)
        {
            instruction = new PayWithCardInstruction(request.Card.ToCardDetails());
        }
        else
        {
            throw new PaymentException(PaymentErrorReason.Validation, "Provide either card details or a savedPaymentMethodId to pay.");
        }

        var order = await service.AuthorizeAsync(request.BuyerId, request.OrderId, instruction);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Order = PaymentApiMapper.ToDto(order)
        };
        return Results.Ok(response);
    }
}

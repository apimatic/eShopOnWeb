using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One of the caller's saved cards (POST /api/payment-methods) …</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>… or card details for a one-off payment. Exactly one of the two.</summary>
    public CardInput? Card { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public override string ToString() => $"Pay order {OrderId} with {(Card is null ? $"saved card {PaymentMethodId}" : Card.ToString())}";
}

/// <summary>
/// Authorizes (holds, does not take) the order total on a card or a saved card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, PaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PaymentActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PaymentService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var instrument = new PaymentInstrument(request.Card?.ToCardDetails(), request.PaymentMethodId);
        // Not tied to the client connection: a money movement, once started, is driven to a settled state.
        var result = await service.PayAsync(request.BuyerId, request.OrderId, instrument, CancellationToken.None);
        return Results.Ok(PaymentActionResponses.From(request, result));
    }
}

internal static class PaymentActionResponses
{
    public static PaymentActionResponse From(BaseRequest request, PaymentOperationResult result) => new(request.CorrelationId())
    {
        OrderId = result.Order.Id,
        OrderStatus = result.Order.Status.ToString(),
        AlreadyDone = result.AlreadyDone,
        Order = OrderDtoMapper.ToDto(result.Order, result.Payment, result.Payment?.Currency ?? string.Empty),
    };
}

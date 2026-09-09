using System.Security.Claims;
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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total for the signed-in shopper, paying with a one-off card or one of
/// the shopper's saved cards. Does not take the money — that happens at fulfilment. Idempotent.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public PayOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct,
                IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.UserId = user.GetUserId();
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var card = request.Card?.ToCardDetails();
        var order = await service.PayAsync(request.UserId, request.OrderId, card,
            request.PaymentMethodId, request.Cancellation);
        return Results.Ok(OrderPaymentDto.From(order, _settings.Currency));
    }
}

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="PaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of raw card details.</summary>
    public int? PaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string UserId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

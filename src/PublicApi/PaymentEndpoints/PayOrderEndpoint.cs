using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string CallerUsername { get; set; } = string.Empty;
}

/// <summary>
/// Authorizes (holds) the order total. Does not take the money — that happens at fulfilment.
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
                request.CallerUsername = CallerIdentity.RequireUsername(user);
                return await HandleAsync(request, service);
            })
            .Produces<PaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var payment = await service.AuthorizeAsync(
            request.CallerUsername, request.OrderId, request.Card?.ToCardDetails(), request.SavedPaymentMethodId);

        return Results.Ok(PaymentDto.From(OrderStatus.Authorized, payment));
    }
}

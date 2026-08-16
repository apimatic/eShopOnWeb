using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total against a one-off card or a saved card. Does not take the money.
/// Idempotent per order: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var card = request.Card?.ToCardPaymentDetails();
        var payment = await service.PayOrderAsync(request.BuyerId, request.OrderId, card, request.SavedPaymentMethodId, ct);
        return Results.Ok(payment);
    }
}

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

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with SavedPaymentMethodId.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// Authorizes (places a hold for) the order total. Does not take the money — that happens at
/// fulfilment. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
             CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var card = request.Card?.ToPaymentCard();
                var payment = await service.AuthorizeAsync(orderId, buyerId, card, request.SavedPaymentMethodId, ct);
                return Results.Ok(PaymentStateResponse.From(payment, request.CorrelationId()));
            })
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.Empty as IResult);
}

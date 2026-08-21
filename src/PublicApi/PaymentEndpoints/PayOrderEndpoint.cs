using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes an order's total (places a hold on the money; does not take it). Pays with either card
/// details for a one-off payment, or a saved card. Idempotent — a double-click never holds twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.AuthorizeOrderAsync(
            request.BuyerId, request.OrderId, request.Card?.ToCardDetails(), request.SavedPaymentMethodId);

        return Results.Ok(PaymentResponse.From(result));
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

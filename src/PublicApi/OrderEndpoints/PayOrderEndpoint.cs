using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    /// <summary>One-off card details. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (places a hold for) the order total. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Authorizes payment for an order (holds funds)", Tags = new[] { "OrderEndpoints" })]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService payments) =>
            {
                var buyerId = user.BuyerId();
                var card = request.Card is { } c && c.IsPopulated() ? c.ToCardDetails() : null;

                var order = await payments.AuthorizeAsync(orderId, buyerId, card, request.SavedPaymentMethodId);

                return Results.Ok(new PayOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Payment = OrderMapper.ToDto(order.Payment!)
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

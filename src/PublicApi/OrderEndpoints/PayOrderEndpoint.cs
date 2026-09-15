using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total. The request either carries raw card details for a one-off
/// payment, or names one of the shopper's saved cards. The call is idempotent in effect: a
/// double-click never authorizes the shopper twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest? request,
                IOrderPaymentService orderPaymentService,
                IPaymentGateway gateway,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request ??= new PayOrderRequest();
                var buyerId = user.GetBuyerId();

                var instruction = new PaymentInstruction(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
                var order = await orderPaymentService.AuthorizeAsync(buyerId, orderId, instruction, cancellationToken);

                var response = new PayOrderResponse
                {
                    OrderId = order.Id,
                    Order = PaymentViewMapper.ToView(order, gateway.Currency)
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class PayOrderRequest
{
    /// <summary>Raw card details for a one-off payment. Mutually exclusive with a saved card.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead of a raw card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public OrderView Order { get; set; } = new();
}

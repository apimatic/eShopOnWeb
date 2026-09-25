using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record PayOrderRequest(string BuyerId, int OrderId, PayOrderBody Body, CancellationToken Ct);

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total with a card or a saved card.</summary>
public class OrdersPayEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, PayOrderBody body, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new PayOrderRequest(user.BuyerId(), orderId, body, ct), service))
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var card = request.Body.Card?.ToCardInput();
        var view = await service.PayAsync(request.BuyerId, request.OrderId, card, request.Body.SavedPaymentMethodId, request.Ct);
        return Results.Ok(view);
    }
}

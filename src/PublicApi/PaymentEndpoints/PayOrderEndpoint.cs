using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Authorizes (holds) an order's total using one-off card details or a saved card.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);

        var hasCard = request.Card is not null && request.Card.HasAnyCardData;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
            throw new PaymentValidationException("Provide either one-off card details or a saved payment method id, not both or neither.");

        var card = hasCard ? request.Card!.ToCardDetails() : null;

        var view = await service.PayAsync(buyerId, request.OrderId, card, request.SavedPaymentMethodId, CancellationToken.None);
        return Results.Ok(OrderPaymentResponse.From(view));
    }
}

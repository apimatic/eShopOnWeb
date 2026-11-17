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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = CreateOrderEndpoint.GetBuyerId(user);
                var card = request.Card ?? (request.HasFlatCardFields ? request : null);
                var savedPaymentMethodId = request.PaymentMethodId ?? request.SavedPaymentMethodId;

                var details = await paymentService.AuthorizeOrderAsync(
                    orderId,
                    buyerId,
                    card?.ToCardDetails(),
                    savedPaymentMethodId,
                    request.IdempotencyKey,
                    cancellationToken);

                return Results.Ok(OrderDto.From(details));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

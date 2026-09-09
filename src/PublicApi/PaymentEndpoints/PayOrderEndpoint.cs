using System;
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

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total for the shopper, using either
/// raw card details or one of the shopper's saved cards. Does not capture. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IPaymentOrderService service,
                CancellationToken ct) =>
            {
                try
                {
                    var order = await service.AuthorizeAsync(
                        user.GetBuyerId(), orderId, request.Card?.ToCardDetails(), request.PaymentMethodId, ct);
                    return Results.Ok(new { orderId = order.Id, order = OrderDto.From(order) });
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderPaymentEndpoints");
    }
}

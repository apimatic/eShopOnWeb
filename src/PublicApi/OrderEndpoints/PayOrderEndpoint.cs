using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorize (hold) the order total. This never captures money; capture happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var order = await paymentService.PayOrderAsync(
            user.GetUserName(),
            request.OrderId,
            request.ToInput());

        var response = new PayOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = OrderResponseMapper.ToPaymentDto(order),
            Order = OrderResponseMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}

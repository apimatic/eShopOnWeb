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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, request, service, user, cancellationToken);
            })
            .Produces<PayOrderResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(
        int orderId,
        PayOrderRequest request,
        IOrderPaymentService service,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.PayAsync(
            orderId,
            buyerId,
            request.Card?.ToDetails(),
            request.PaymentMethodId,
            cancellationToken);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            Order = OrderPaymentDto.From(order)
        };
        return Results.Ok(response);
    }
}

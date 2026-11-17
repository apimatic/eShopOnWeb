using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public PayOrderEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, ClaimsPrincipal user, PayOrderRequest request, CancellationToken cancellationToken) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, user, cancellationToken);
                })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        PayOrderRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var response = await _paymentService.PayOrderAsync(buyerId, request.OrderId, request, cancellationToken);
        return Results.Ok(response);
    }
}

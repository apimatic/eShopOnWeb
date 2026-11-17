using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public PayOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, user, cancellationToken);
                })
            .Produces<PaymentView>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        PayOrderRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var result = await EndpointBudget.RunAsync(
            ct => _payments.PayOrderAsync(buyerId, request, ct),
            cancellationToken);
        return Results.Ok(result);
    }
}

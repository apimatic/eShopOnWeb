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

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public FulfilOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, user, cancellationToken))
            .Produces<PaymentView>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        FulfilOrderRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        _ = user;
        var result = await EndpointBudget.RunAsync(
            ct => _payments.FulfilOrderAsync(request.OrderId, ct),
            cancellationToken);
        return Results.Ok(result);
    }
}

public sealed class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

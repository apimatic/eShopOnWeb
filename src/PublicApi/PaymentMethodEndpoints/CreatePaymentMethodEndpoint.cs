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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public CreatePaymentMethodEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(request, user, cancellationToken))
            .Produces<SavedPaymentMethodView>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SavePaymentMethodRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = OrderEndpoints.EndpointIdentity.GetBuyerId(user);
        var result = await OrderEndpoints.EndpointBudget.RunAsync(
            ct => _payments.SavePaymentMethodAsync(buyerId, request, ct),
            cancellationToken);
        return Results.Created($"api/payment-methods/{result.PaymentMethodId}", result);
    }
}

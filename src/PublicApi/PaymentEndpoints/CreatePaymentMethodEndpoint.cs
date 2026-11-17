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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public CreatePaymentMethodEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, SavePaymentMethodRequest request, CancellationToken cancellationToken) =>
                    await HandleAsync(request, user, cancellationToken))
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SavePaymentMethodRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var paymentMethod = await _paymentService.CreatePaymentMethodAsync(buyerId, request, cancellationToken);

        return Results.Created($"api/payment-methods/{paymentMethod.PaymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = paymentMethod.PaymentMethodId,
            PaymentMethod = paymentMethod
        });
    }
}

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

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public DeletePaymentMethodEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int paymentMethodId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(
                        new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId },
                        user,
                        cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DeletePaymentMethodRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        await _paymentService.DeletePaymentMethodAsync(buyerId, request.PaymentMethodId, cancellationToken);
        return Results.NoContent();
    }
}

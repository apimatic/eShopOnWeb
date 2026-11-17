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

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public DeletePaymentMethodEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int paymentMethodId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, user, cancellationToken))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DeletePaymentMethodRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = OrderEndpoints.EndpointIdentity.GetBuyerId(user);
        await OrderEndpoints.EndpointBudget.RunAsync(
            ct => _payments.DeletePaymentMethodAsync(buyerId, request.PaymentMethodId, ct),
            cancellationToken);
        return Results.NoContent();
    }
}

public sealed class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes a saved card and disables it at Finix so it can no longer be used.
/// </summary>
public class DeletePaymentMethodEndpoint
    : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public DeletePaymentMethodEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId },
                    user, cancellationToken);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await _orderPaymentService.DeletePaymentMethodAsync(
            user.Identity!.Name!, request.PaymentMethodId, cancellationToken);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId
        });
    }
}

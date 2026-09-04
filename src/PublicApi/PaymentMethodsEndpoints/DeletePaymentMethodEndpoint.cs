using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

/// <summary>
/// Removes one of the caller's saved cards — locally and from the provider vault, so it
/// can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public DeletePaymentMethodEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string paymentMethodId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(paymentMethodId, user);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodsEndpoints");
    }

    public async Task<IResult> HandleAsync(string paymentMethodId, ClaimsPrincipal user)
    {
        var buyerId = AuthenticatedUser.RequireIdentity(user);

        await _payments.DeleteCardAsync(buyerId, paymentMethodId);

        return Results.NoContent();
    }
}

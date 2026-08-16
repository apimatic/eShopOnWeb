using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Returns the caller's own saved cards, described safely.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(buyerId, service, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(string buyerId, ISavedCardService service) =>
        HandleAsync(buyerId, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(string buyerId, ISavedCardService service, CancellationToken ct)
    {
        var paymentMethods = await service.ListAsync(buyerId, ct);
        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = paymentMethods.Select(pm => pm.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}

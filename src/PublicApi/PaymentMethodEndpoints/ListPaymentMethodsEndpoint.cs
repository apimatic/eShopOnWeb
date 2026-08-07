using System.Linq;
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

/// <summary>Returns the signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public ListPaymentMethodsEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(user, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var paymentMethods = await _paymentMethodService.ListAsync(buyerId, ct);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = paymentMethods.Select(PaymentMethodDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the caller's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService) => await HandleAsync(savedCardService))
            .Produces<PaymentMethodsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await savedCardService.ListForBuyerAsync(buyerId);
        if (!result.IsSuccess)
        {
            return result.ToProblem();
        }

        var response = new PaymentMethodsResponse
        {
            PaymentMethods = result.Value.Select(pm => pm.ToResponse()).ToList()
        };
        return Results.Ok(response);
    }
}

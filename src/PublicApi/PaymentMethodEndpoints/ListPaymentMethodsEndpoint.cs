using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentMethodService service) => await HandleAsync(service))
            .Produces<List<SavedCardResponse>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var methods = await service.GetCardsForBuyerAsync(buyerId);
        return Results.Ok(methods.Select(PaymentApiMapper.ToResponse).ToList());
    }
}

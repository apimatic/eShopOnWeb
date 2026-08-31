using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
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
            (ISavedPaymentMethodService savedPaymentMethodService) =>
            {
                return await HandleAsync(savedPaymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedPaymentMethodService savedPaymentMethodService)
    {
        var response = new ListPaymentMethodsResponse();
        var buyerId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

        var saved = await savedPaymentMethodService.ListAsync(buyerId);
        response.PaymentMethods = saved.Select(PaymentMethodDto.FromSavedPaymentMethod).ToList();

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}

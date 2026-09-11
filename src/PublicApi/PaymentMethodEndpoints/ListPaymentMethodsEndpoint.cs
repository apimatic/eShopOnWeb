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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>The caller's saved cards. Shopper-scoped.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service) =>
                await HandleAsync(user, service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IPaymentMethodService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var methods = await service.ListAsync(buyerId);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => new SavedCardDto
            {
                PaymentMethodId = m.Id,
                CardBrand = m.CardBrand,
                CardLast4 = m.CardLast4,
                CardExpiry = m.CardExpiry,
                Description = m.Description,
                CreatedAt = m.CreatedAt.ToString("o")
            }).ToList()
        };

        return Results.Ok(response);
    }
}

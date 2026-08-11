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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentMethodService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var methods = await service.GetCardsAsync(buyerId);
        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => m.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}

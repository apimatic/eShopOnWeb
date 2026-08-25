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

/// <summary>Lists the signed-in shopper's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.Identity!.Name! }, paymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var methods = await paymentMethodService.GetPaymentMethodsAsync(request.BuyerId);
        response.PaymentMethods = methods.Select(m => new PaymentMethodDto
        {
            PaymentMethodId = m.Id,
            CardBrand = m.CardBrand,
            Last4 = m.Last4,
            Expiry = m.Expiry,
            Alias = m.Alias,
            CreatedAt = m.CreatedAt
        }).ToList();
        return Results.Ok(response);
    }
}

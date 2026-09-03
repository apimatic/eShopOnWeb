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

/// <summary>Lists the signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IPaymentMethodService service) =>
                await HandleAsync(http, service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext http, IPaymentMethodService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var cards = await service.ListAsync(buyerId, http.RequestAborted);

            var response = new ListPaymentMethodsResponse
            {
                PaymentMethods = cards.Select(pm => new PaymentMethodDto
                {
                    PaymentMethodId = pm.Id,
                    CardBrand = pm.CardBrand,
                    LastFourDigits = pm.LastFourDigits,
                    Expiry = pm.Expiry,
                    CreatedAt = pm.CreatedAt
                }).ToList()
            };
            return Results.Ok(response);
        });
}

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = http.RequireBuyerId() }, methods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService methods)
    {
        var saved = await methods.ListAsync(request.BuyerId, default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = saved.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.PaymentTokenId,
                LastDigits = m.LastDigits,
                Brand = m.Brand,
                Expiry = m.Expiry,
                Name = m.CardholderName
            }).ToList()
        });
    }
}

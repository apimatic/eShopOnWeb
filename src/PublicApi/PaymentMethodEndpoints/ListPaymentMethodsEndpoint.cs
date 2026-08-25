using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<PaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<PaymentMethod> methodRepository, HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                var spec = new PaymentMethodsByBuyerIdSpec(buyer);
                var methods = await methodRepository.ListAsync(spec);

                var result = new List<object>();
                foreach (var m in methods)
                {
                    result.Add(new
                    {
                        paymentMethodId = m.Id,
                        lastFour = m.CardLastFour,
                        brand = m.CardBrand,
                        expiry = m.CardExpiry,
                        createdAt = m.CreatedAt
                    });
                }

                return Results.Ok(result);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<PaymentMethod> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}

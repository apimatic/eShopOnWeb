using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Last4 { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, IReadRepository<SavedPaymentMethod> pmRepo) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var methods = await pmRepo.ListAsync(spec);

                var result = methods.Select(pm => new PaymentMethodDto
                {
                    PaymentMethodId = pm.Id,
                    Last4 = pm.Last4,
                    Brand = pm.Brand,
                    Expiry = pm.Expiry
                }).ToList();

                return Results.Ok(result);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

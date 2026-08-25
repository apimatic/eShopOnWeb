using System;
using System.Linq;
using System.Threading;
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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<SavedPaymentMethod> repo,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var methods = await repo.ListAsync(spec, ct);

                var result = new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(m => new SavedCardDto
                    {
                        PaymentMethodId = m.Id,
                        Last4 = m.Last4,
                        Brand = m.Brand,
                        Expiry = m.Expiry
                    }).ToList()
                };

                return Results.Ok(result);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(string request, IRepository<SavedPaymentMethod> service)
        => throw new NotImplementedException();
}

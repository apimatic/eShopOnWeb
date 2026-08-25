using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListCardsEndpoint : IEndpoint<IResult, string, IReadRepository<Buyer>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReadRepository<Buyer> buyerRepo, HttpContext httpContext) =>
            {
                var buyerId = httpContext.User.Identity!.Name!;
                return await HandleAsync(buyerId, buyerRepo);
            })
            .Produces<ListCardsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IReadRepository<Buyer> buyerRepo)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = (await buyerRepo.ListAsync(spec)).FirstOrDefault();

        var methods = buyer?.PaymentMethods
            .Select(pm => new PaymentMethodDto
            {
                Id = pm.Id,
                Last4 = pm.Last4,
                CardBrand = pm.CardBrand,
                ExpiryMonth = pm.ExpiryMonth,
                ExpiryYear = pm.ExpiryYear,
                Alias = pm.Alias
            }).ToList() ?? new();

        return Results.Ok(new ListCardsResponse { PaymentMethods = methods });
    }
}

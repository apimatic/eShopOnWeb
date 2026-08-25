using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IReadRepository<UserPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IReadRepository<UserPaymentMethod> pmRepo) =>
            {
                var userName = httpContext.User.Identity!.Name!;
                var spec = new UserPaymentMethodsByUserIdSpec(userName);
                var methods = await pmRepo.ListAsync(spec);
                var result = methods.Select(m => new PaymentMethodDto(m.Id, m.Last4, m.Brand, m.Expiry)).ToList();
                return Results.Ok(result);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IReadRepository<UserPaymentMethod> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class ListPaymentMethodsRequest : BaseRequest { }

public record PaymentMethodDto(int PaymentMethodId, string Last4, string Brand, string Expiry);

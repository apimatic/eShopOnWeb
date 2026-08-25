using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<SavedPaymentMethod> repository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = buyerId }, repository);
            })
            .Produces<ListPaymentMethodsResponse>(200)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<SavedPaymentMethod> repository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new SavedPaymentMethodsByBuyerSpec(request.BuyerId);
        var methods = await repository.ListAsync(spec);

        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.Id,
                CardBrand = m.CardBrand,
                Last4 = m.Last4,
                CardExpiry = m.CardExpiry,
                CardholderName = m.CardholderName,
                CreatedAt = m.CreatedAt
            }).ToList()
        });
    }
}

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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Lists the caller's saved cards", Tags = new[] { "PaymentMethodEndpoints" })]
            async (ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                var buyerId = user.BuyerId();
                var methods = await service.ListAsync(buyerId);

                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(m => new PaymentMethodDto
                    {
                        PaymentMethodId = m.Id,
                        Brand = m.CardBrand,
                        Last4 = m.Last4,
                        Expiry = m.Expiry,
                        CardholderName = m.CardholderName
                    }).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

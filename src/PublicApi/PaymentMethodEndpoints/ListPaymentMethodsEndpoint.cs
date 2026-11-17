using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async ([Microsoft.AspNetCore.Mvc.FromServices] IPaymentMethodService service) => await HandleAsync(service))
            .Produces<IReadOnlyList<PaymentMethodResponse>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentMethodService service)
    {
        var response = await service.ListAsync(default);
        return Results.Ok(response);
    }
}

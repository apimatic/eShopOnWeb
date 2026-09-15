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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IOrderPaymentService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListPaymentMethodsApiResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderPaymentService service)
    {
        var methods = await service.ListPaymentMethodsAsync(http.RequireBuyerId());
        return Results.Ok(new ListPaymentMethodsApiResponse
        {
            PaymentMethods = methods.Select(PaymentMethodMapper.ToDto).ToList()
        });
    }
}

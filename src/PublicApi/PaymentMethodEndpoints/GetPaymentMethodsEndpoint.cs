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

public class GetPaymentMethodsEndpoint : IEndpoint<IResult, GetPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService paymentMethods, HttpContext httpContext) =>
            {
                return await HandleAsync(new GetPaymentMethodsRequest(), paymentMethods, httpContext);
            })
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(GetPaymentMethodsRequest request, ISavedPaymentMethodService paymentMethods) =>
        HandleAsync(request, paymentMethods, null!);

    private async Task<IResult> HandleAsync(
        GetPaymentMethodsRequest request,
        ISavedPaymentMethodService paymentMethods,
        HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredUserName();
        var methods = await paymentMethods.ListAsync(buyerId);
        return Results.Ok(new PaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMapping.ToPaymentMethodResponse).ToList()
        });
    }
}

public class GetPaymentMethodsRequest
{
}

public class PaymentMethodsResponse
{
    public System.Collections.Generic.List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

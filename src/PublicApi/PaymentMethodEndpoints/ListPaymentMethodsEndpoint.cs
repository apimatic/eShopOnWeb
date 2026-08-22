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

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(paymentMethods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedPaymentMethodService paymentMethods)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new System.InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.User.GetBuyerId();
        var list = await paymentMethods.ListAsync(buyerId, httpContext.RequestAborted);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = list.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.Id,
                LastDigits = m.LastDigits,
                Brand = m.Brand,
                Expiry = m.Expiry
            }).ToList()
        };

        return Results.Ok(response);
    }
}

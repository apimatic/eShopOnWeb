using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IPaymentCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(EndpointUser.BuyerId(_httpContextAccessor.HttpContext!), payments);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentCheckoutService payments)
    {
        var methods = await payments.ListSavedCardsAsync(buyerId);
        return Results.Ok(new PaymentMethodListResponse
        {
            PaymentMethods = methods.Select(OrderResponseMapper.Map).ToList()
        });
    }
}

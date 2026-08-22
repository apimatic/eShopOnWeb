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

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
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
            (IPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), paymentMethods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService paymentMethods)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        var saved = await paymentMethods.ListAsync(buyerId, default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = saved.Select(s => new PaymentMethodResponse
            {
                PaymentMethodId = s.Id,
                Brand = s.Brand,
                LastDigits = s.LastDigits,
                Expiry = s.Expiry,
                CardholderName = s.CardholderName
            }).ToList()
        });
    }
}

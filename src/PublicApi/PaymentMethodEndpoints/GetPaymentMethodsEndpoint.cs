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

public class GetPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedCardService cards) => await HandleAsync(cards))
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService cards)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext);
        var methods = await cards.ListAsync(buyerId);
        return Results.Ok(new PaymentMethodListResponse
        {
            PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
        });
    }
}

public class PaymentMethodListResponse
{
    public System.Collections.Generic.List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

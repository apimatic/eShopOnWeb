using System.Collections.Generic;
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

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
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
            (ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var saved = await service.ListAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = saved.Select(m => new PaymentMethodResponse
            {
                PaymentMethodId = m.Id,
                Brand = m.Brand,
                LastDigits = m.LastDigits,
                Expiry = m.Expiry,
                CardholderName = m.CardholderName
            }).ToList()
        });
    }
}

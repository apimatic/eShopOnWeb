using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedPaymentMethodService service, HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext.RequireBuyerId(), service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedPaymentMethodService service)
    {
        var methods = await service.ListAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodDto.From).ToList()
        });
    }
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand ?? string.Empty,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

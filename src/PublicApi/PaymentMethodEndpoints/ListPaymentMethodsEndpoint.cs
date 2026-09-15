using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>The caller's own saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ISavedCardService service) =>
            {
                return await HandleAsync(
                    new ListPaymentMethodsRequest { BuyerId = PaymentMapper.GetBuyerId(http) }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service)
    {
        var cards = await service.ListCardsAsync(request.BuyerId);
        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

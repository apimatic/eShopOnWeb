using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, MyOrdersRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ISavedCardService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = CallerIdentity.BuyerId(http), Ct = ct }, service);
            })
            .Produces<PaymentMethodsResponse>()
            .WithTags("PayPalPaymentMethods");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, ISavedCardService service)
    {
        var cards = await service.GetCardsAsync(request.BuyerId, request.Ct);
        var response = new PaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = cards.Select(c => new SavedCardResponse(request.CorrelationId())
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                LastDigits = c.LastDigits,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName,
                CreatedAt = c.CreatedAt,
            }).ToList(),
        };
        return Results.Ok(response);
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService service)
    {
        BillingAddress? billing = null;
        if (request.Card.BillingAddress is not null)
        {
            var a = request.Card.BillingAddress;
            billing = new BillingAddress(a.AddressLine1, a.AddressLine2, a.AdminArea2, a.AdminArea1, a.PostalCode, a.CountryCode);
        }

        var card = new CardDetails(request.Card.Name, request.Card.Number, request.Card.Expiry, request.Card.SecurityCode, billing);
        var saved = await service.SaveCardAsync(request.BuyerId, card);
        var response = PaymentMethodMapper.ToResponse(saved, request.CorrelationId());
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

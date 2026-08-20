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

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext http, ISavedPaymentMethodService service) =>
            {
                request.BuyerId = Caller.UserName(http);
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(request.BuyerId!, CardMapping.ToCardDetails(request.Card));
        var dto = PaymentMethodDto.From(saved);
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        };
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", response);
    }
}

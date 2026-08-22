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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedCardService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service, HttpContext http)
    {
        var buyerId = EndpointIdentity.RequireUserName(http);
        var saved = await service.SaveAsync(buyerId, EndpointIdentity.ToCard(request.Card), http.RequestAborted);
        var response = PaymentMethodResponseMapper.From(saved);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

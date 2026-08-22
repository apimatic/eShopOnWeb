using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(request, user, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service) =>
        HandleAsync(request, new ClaimsPrincipal(), service);

    private async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        ClaimsPrincipal user,
        ISavedPaymentMethodService service)
    {
        var saved = await service.SaveAsync(user.RequireBuyerId(), request.Card.ToDetails());
        var response = PaymentMethodResponse.From(saved, request.CorrelationId());
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        ISavedPaymentMethodService service,
        ClaimsPrincipal user)
    {
        if (request.Card == null)
        {
            throw new OrderPaymentException(400, "Card details are required.");
        }

        var saved = await service.SaveCardAsync(user.GetRequiredUserName(), request.Card.ToCardPaymentSource());
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDtoMapper.ToDto(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Save a card for the signed-in shopper and return a safe description of it.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, PaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, PaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, PaymentService paymentService)
    {
        if (request.Card is null)
        {
            return Results.BadRequest(new { error = "Card details are required." });
        }

        try
        {
            var ownerId = EndpointHelpers.GetOwnerId(_httpContextAccessor.HttpContext!.User);
            var savedCard = await paymentService.SaveCardAsync(ownerId, request.Card.ToCardDetails());

            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = savedCard.PaymentMethodId,
                PaymentMethod = SavedCardDto.From(savedCard)
            };

            return Results.Created($"api/payment-methods/{savedCard.PaymentMethodId}", response);
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}

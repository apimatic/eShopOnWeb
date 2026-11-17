using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly FinixPaymentService _payments;

    public SavePaymentMethodEndpoint(FinixPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SavePaymentMethodRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        try
        {
            var card = request.ToCardDetails();
            var savedCard = await _payments.SaveCardAsync(buyerId, card, CancellationToken.None);
            var dto = PaymentResponseMapper.ToSavedPaymentMethodResponse(savedCard);

            return Results.Created(
                $"api/payment-methods/{savedCard.PublicId}",
                new CreatePaymentMethodResponse
                {
                    PaymentMethodId = savedCard.PublicId,
                    PaymentMethod = dto
                });
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }
    }
}

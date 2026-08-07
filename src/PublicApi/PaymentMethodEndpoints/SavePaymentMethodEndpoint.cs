using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card to the authenticated shopper's PayPal vault for reuse on later orders.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(request, paymentMethodService, user, cancellationToken))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("PaymentMethodEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        SavePaymentMethodRequest request,
        IPaymentMethodService paymentMethodService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var ownerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.ExpiryMonthYear))
        {
            return Results.Problem(detail: "Card number and expiry (YYYY-MM) are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await paymentMethodService.SaveCardAsync(ownerId, request.Card.ToCardDetails(), request.Alias, cancellationToken);

        if (result.Outcome != SaveCardOutcome.Saved || result.PaymentMethod is null)
        {
            return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = result.PaymentMethod.Id,
            PaymentMethod = PaymentMethodDto.FromEntity(result.PaymentMethod)
        };

        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}

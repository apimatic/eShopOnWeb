using System.Security.Claims;
using System.Threading;
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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService service, CancellationToken ct) =>
            {
                var card = PayOrderEndpoint.ToCard(request.Card);
                if (card == null)
                    return Results.BadRequest(new { message = "Card details are required." });

                var saved = await service.SaveAsync(user.GetBuyerId(), card, ct);
                return Results.Created($"api/payment-methods/{saved.PaymentTokenId}", PaymentMethodResponse.From(saved));
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
                await HandleAsync(request, payments, user))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CardRequest request, IOrderPaymentService payments)
        => HandleAsync(request, payments, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CardRequest request, IOrderPaymentService payments, ClaimsPrincipal user)
    {
        var saved = await payments.SaveCardAsync(PaymentApiMapper.BuyerId(user), PaymentApiMapper.ToCard(request));
        return Results.Created($"api/payment-methods/{saved.Id}", PaymentApiMapper.FromSavedCard(saved));
    }
}

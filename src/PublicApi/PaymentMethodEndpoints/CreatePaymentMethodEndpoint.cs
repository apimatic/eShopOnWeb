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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, service, user, cancellationToken);
            })
            .Produces<CreatePaymentMethodResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        IPaymentMethodService service,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var method = await service.SaveCardAsync(buyerId, request.Card.ToDetails(), cancellationToken);
        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

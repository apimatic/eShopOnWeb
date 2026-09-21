using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint
    : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service,
             ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        var result = await service.RegisterAsync(request.BuyerId, request.PhoneNumber, ct);
        if (!result.Registered)
        {
            return Results.BadRequest(new { message = result.Reason });
        }

        var response = new RegisterContactNumberResponse(result.ContactNumberId!.Value, result.CanonicalNumber!);
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

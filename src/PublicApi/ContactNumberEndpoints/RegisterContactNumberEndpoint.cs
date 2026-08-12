using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated and canonicalised
/// by the provider up front; an unusable destination is rejected here.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service, CancellationToken cancellationToken) =>
            {
                var owner = CurrentUser.GetUserName(user);
                if (owner is null)
                {
                    return Results.Unauthorized();
                }

                var result = await service.RegisterAsync(owner, request?.PhoneNumber ?? string.Empty, cancellationToken);
                if (!result.Succeeded)
                {
                    var message = result.Error == RegisterContactNumberError.Missing
                        ? "A phone number is required."
                        : "The number is not a usable destination.";
                    return Results.BadRequest(new { message });
                }

                var response = new RegisterContactNumberResponse(request!.CorrelationId())
                {
                    ContactNumberId = result.ContactNumberId!.Value,
                    PhoneNumber = result.CanonicalNumber!
                };

                return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}

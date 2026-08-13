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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. The number is
/// validated against the provider and stored in its canonical form; an unusable number is rejected here.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                IContactNumberService service,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var ownerId = CallerIdentity.GetOwnerId(user);
                var contactNumber = await service.RegisterAsync(ownerId, request.PhoneNumber, cancellationToken);

                var dto = ContactNumberDto.From(contactNumber);
                var response = new RegisterContactNumberResponse
                {
                    ContactNumberId = contactNumber.Id,
                    ContactNumber = dto
                };
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}

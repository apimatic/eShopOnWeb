using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here, and what gets stored is the provider's canonical form.
/// Scoped services are resolved per request via the route delegate (never captured in the ctor).
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IPhoneNumberLookup lookup, IRepository<ContactNumber> repository) =>
            {
                return await HandleAsync(request, user, lookup, repository);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    private static async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user, IPhoneNumberLookup lookup, IRepository<ContactNumber> repository)
    {
        var ownerId = user.UserId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.Problem("A phone number is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await lookup.LookupAsync(request.PhoneNumber);
        if (!result.Valid || string.IsNullOrEmpty(result.E164))
        {
            // Rejected here rather than at the moment a message would fail to go out.
            return Results.Problem("The provider does not consider this a usable destination number.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Store the provider's own canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(ownerId, result.E164);
        await repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            ContactNumber = ContactNumberDto.From(contactNumber)
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

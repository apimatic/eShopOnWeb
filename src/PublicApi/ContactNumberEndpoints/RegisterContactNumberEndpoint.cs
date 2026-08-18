using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates and canonicalizes
/// the number up front: an unusable destination is rejected here, and what is stored is the
/// provider's canonical form, not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IRepository<ContactNumber>>
{
    private readonly ISmsSender _sms;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(ISmsSender sms, IHttpContextAccessor httpContextAccessor)
    {
        _sms = sms;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IRepository<ContactNumber> repository) =>
                await HandleAsync(request, repository))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IRepository<ContactNumber> repository)
    {
        var ownerId = _httpContextAccessor.GetCallerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.Problem("A phone number is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var lookup = await _sms.LookupAsync(request.PhoneNumber);
        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return Results.Problem("The number is not a usable SMS destination.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Idempotent registration: if this shopper already has the canonical number, return it.
        var existing = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        var already = existing.FirstOrDefault(c => c.PhoneNumber == lookup.CanonicalNumber);
        if (already is not null)
        {
            return Results.Ok(new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber
            });
        }

        var contactNumber = new ContactNumber(ownerId, lookup.CanonicalNumber!);
        contactNumber = await repository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

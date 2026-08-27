using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IRepository<ContactNumber>, ITextMessagingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository, ITextMessagingService messagingService) =>
            {
                return await HandleAsync(request, contactNumberRepository, messagingService);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository, ITextMessagingService messagingService)
    {
        var ownerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        ValidatedPhoneNumber validated;
        try
        {
            validated = await messagingService.ValidatePhoneNumberAsync(request.PhoneNumber);
        }
        catch (InvalidPhoneNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (TextMessagingException)
        {
            return Results.Problem("The messaging provider could not be reached to validate the number.", statusCode: 502);
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        if (existing.Any(c => c.PhoneNumber == validated.CanonicalNumber))
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = await contactNumberRepository.AddAsync(new ContactNumber(ownerId, validated.CanonicalNumber));

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

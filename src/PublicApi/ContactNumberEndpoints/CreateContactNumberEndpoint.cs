using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly ITwilioLookupClient _lookup;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IAppLogger<CreateContactNumberEndpoint> _logger;

    public CreateContactNumberEndpoint(
        ITwilioLookupClient lookup,
        IRepository<ContactNumber> contactNumbers,
        IAppLogger<CreateContactNumberEndpoint> logger)
    {
        _lookup = lookup;
        _contactNumbers = contactNumbers;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, ClaimsPrincipal user) =>
                await HandleAsync(request, user))
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var buyerId = user.GetRequiredBuyerId();
        var lookup = await _lookup.LookupAsync(request.PhoneNumber);
        if (!lookup.Succeeded)
        {
            _logger.LogWarning("Contact number lookup is unavailable.");
            return Results.Json(new { message = "The number could not be validated right now." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            return Results.BadRequest(new { message = "This number is not a usable destination." });
        }

        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndE164Specification(buyerId, lookup.CanonicalPhoneNumber));
        if (existing != null)
        {
            return Results.Created($"api/contact-numbers/{existing.Id}", new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = existing.Id,
                PhoneNumber = existing.E164Number
            });
        }

        var created = await _contactNumbers.AddAsync(new ContactNumber(buyerId, lookup.CanonicalPhoneNumber));
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.E164Number
        };

        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper. A number the provider does
/// not consider usable is rejected here (not when a later message fails), and what is stored is the provider's
/// own canonical E.164 form, not the raw input.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IRepository<ContactNumber>>
{
    private readonly IPhoneNumberValidator _validator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IPhoneNumberValidator validator, IHttpContextAccessor httpContextAccessor)
    {
        _validator = validator;
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
        => await HandleAsync(request, repository, CancellationToken.None);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IRepository<ContactNumber> repository, CancellationToken cancellationToken)
    {
        var owner = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(owner))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        var validation = await _validator.ValidateAsync(request.PhoneNumber, request.CountryCode, cancellationToken);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.E164))
        {
            return Results.BadRequest(new
            {
                message = "The number is not a usable destination and was not registered.",
                errors = validation.Errors
            });
        }

        var canonical = validation.E164!;

        // Idempotent registration: if the caller already has this number, return the existing record.
        var existing = await repository.ListAsync(new ContactNumbersByOwnerSpecification(owner), cancellationToken);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            return Results.Ok(new RegisterContactNumberResponse
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber
            });
        }

        var created = await repository.AddAsync(new ContactNumber(owner, canonical), cancellationToken);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}

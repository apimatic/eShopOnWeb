using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, IContactNumberService service, HttpContext httpContext) =>
            {
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, contactNumberService, null!);

    private Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, HttpContext httpContext)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var buyerId = httpContext.User.RequireBuyerId();
            var contact = await service.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = contact.Id,
                PhoneNumber = contact.PhoneNumber,
                NationalFormat = contact.NationalFormat,
                CountryCode = contact.CountryCode
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        });
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public string? CountryCode { get; set; }
}

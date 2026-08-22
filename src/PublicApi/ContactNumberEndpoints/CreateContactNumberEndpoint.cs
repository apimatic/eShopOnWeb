using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext http, IContactNumberService service, CancellationToken cancellationToken) =>
            {
                var userName = http.GetUserName();
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, service, userName, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        IContactNumberService service,
        string buyerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await service.RegisterAsync(buyerId, request.PhoneNumber, cancellationToken);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                Number = created.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (UnusablePhoneNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (ProviderUnavailableException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: 502);
        }
    }
}

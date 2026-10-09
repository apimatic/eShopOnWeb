using Microsoft.Extensions.Configuration;
using Square;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using SquareCheck.Display;
using SquareCheck.SignIn;

namespace SquareCheck;

sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        using var ctrlCCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ctrlCCts.Cancel();
        };

        var ct = ctrlCCts.Token;

        try
        {
            var config = BuildConfiguration();
            var settings = SquareSettings.Load(config);

            var flow = new SignInFlow();
            var result = await flow.SignInAsync(settings, ct);

            using var httpClient = new HttpClient();
            var client = new SquareClient(httpClient, new SquareClientOptions
            {
                Environment = settings.SquareEnvironment,
                Oauth2 = new OAuth2AuthorizationCodeCredentials
                {
                    ClientId = settings.ApplicationId,
                    RedirectUri = settings.RedirectUri,
                    PromptForAuthorizationCode = (_, _) => Task.FromResult(string.Empty),
                },
                Oauth2TokenStrategy = new PreObtainedTokenStrategy(result.AccessToken),
                Logging = new LoggingOptions { LoggerFactory = null },
                Retry = RetryOptions.Disabled(),
            });

            var locationsResponse = await client.Locations.ListLocations(cancellationToken: ct);

            var printer = new AccountPrinter();
            printer.Print(result.Merchant, locationsResponse.Locations);

            return 0;
        }
        catch (OperationCanceledException) when (ctrlCCts.IsCancellationRequested)
        {
            return 130;
        }
        catch (SignInTimeoutException)
        {
            Console.Error.WriteLine("Sign-in timed out after 5 minutes.");
            return 2;
        }
        catch (SignInDeclinedException)
        {
            Console.Error.WriteLine("Operator declined access.");
            return 2;
        }
        catch (SquareApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ApiException<RawError> ex)
        {
            Console.Error.WriteLine($"Square refused the request: HTTP {(int)ex.StatusCode}.");
            return 1;
        }
        catch (SdkException ex)
        {
            Console.Error.WriteLine($"Square failed: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    static IConfiguration BuildConfiguration()
    {
        var envMappings = new Dictionary<string, string?>
        {
            ["Square:Environment"] = Environment.GetEnvironmentVariable("SQUARE_ENVIRONMENT"),
            ["Square:ApplicationId"] = Environment.GetEnvironmentVariable("SQUARE_APPLICATION_ID"),
            ["Square:ApplicationSecret"] = Environment.GetEnvironmentVariable("SQUARE_APPLICATION_SECRET"),
            ["Square:RedirectUri"] = Environment.GetEnvironmentVariable("SQUARE_REDIRECT_URI"),
        };

        var nonNull = envMappings
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>()
            .AddInMemoryCollection(nonNull)
            .Build();
    }
}

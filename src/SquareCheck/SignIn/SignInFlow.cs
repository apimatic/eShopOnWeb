using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;
using Square;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Models;
using Square.Requests.Merchants;
using Square.Requests.OAuth;
using Square.Servers;

namespace SquareCheck.SignIn;

internal sealed class SignInFlow
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _timeout;

    internal SignInFlow(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
    }

    internal async Task<SignInResult> SignInAsync(SquareSettings settings, CancellationToken ct)
    {
        var state = Guid.NewGuid().ToString("N");
        var authUrl = BuildAuthUrl(settings, state);

        using var listener = CreateListener(settings.RedirectUri);
        listener.Start();

        OpenBrowser(authUrl);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await WaitForContextAsync(listener, linkedToken);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new SignInTimeoutException();
            }

            var callbackUri = new Uri(settings.RedirectUri);
            if (!string.Equals(
                    context.Request.Url?.AbsolutePath,
                    callbackUri.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                await ServeResponseAsync(context, 404, "text/plain", "Not found.");
                continue;
            }

            var callbackResult = ParseCallback(context.Request.Url!, state);

            switch (callbackResult)
            {
                case CallbackResult.Stale:
                    await ServeHtmlAsync(context, HtmlPages.Stale);
                    continue;

                case CallbackResult.Declined:
                    await ServeHtmlAsync(context, HtmlPages.Declined);
                    throw new SignInDeclinedException();

                case CallbackResult.Success success:
                    return await HandleSuccessAsync(context, settings, success.Code, ct);
            }
        }
    }

    private static async Task<SignInResult> HandleSuccessAsync(
        HttpListenerContext context, SquareSettings settings, string code, CancellationToken ct)
    {
        using var httpClient = new HttpClient();

        var tokenClient = new SquareClient(httpClient, new SquareClientOptions
        {
            Environment = settings.SquareEnvironment,
            Logging = new LoggingOptions { LoggerFactory = null },
            Retry = RetryOptions.Disabled(),
        });

        string accessToken;
        try
        {
            accessToken = await ExchangeCodeAsync(tokenClient, settings, code, ct);
        }
        catch (ApiException<RawError> ex)
        {
            await ServeHtmlAsync(context, HtmlPages.Error);
            throw new SquareApiException(
                $"Square refused the sign-in request: HTTP {(int)ex.StatusCode}.", ex);
        }
        catch (SdkException ex)
        {
            await ServeHtmlAsync(context, HtmlPages.Error);
            throw new SquareApiException($"Square failed during token exchange: {ex.Message}", ex);
        }

        var merchantClient = new SquareClient(httpClient, new SquareClientOptions
        {
            Environment = settings.SquareEnvironment,
            Oauth2 = new OAuth2AuthorizationCodeCredentials
            {
                ClientId = settings.ApplicationId,
                RedirectUri = settings.RedirectUri,
                PromptForAuthorizationCode = (_, _) => Task.FromResult(string.Empty),
            },
            Oauth2TokenStrategy = new PreObtainedTokenStrategy(accessToken),
            Logging = new LoggingOptions { LoggerFactory = null },
            Retry = RetryOptions.Disabled(),
        });

        Merchant merchant;
        try
        {
            merchant = await GetMerchantAsync(merchantClient, ct);
        }
        catch (ApiException<RawError> ex)
        {
            await ServeHtmlAsync(context, HtmlPages.Error);
            throw new SquareApiException(
                $"Square refused the merchant profile request: HTTP {(int)ex.StatusCode}.", ex);
        }
        catch (SdkException ex)
        {
            await ServeHtmlAsync(context, HtmlPages.Error);
            throw new SquareApiException($"Square failed during merchant lookup: {ex.Message}", ex);
        }

        var businessName = merchant.BusinessName ?? merchant.Id ?? "your store";
        await ServeHtmlAsync(context, HtmlPages.Success(businessName));

        return new SignInResult { AccessToken = accessToken, Merchant = merchant };
    }

    internal static CallbackResult ParseCallback(Uri requestUri, string expectedState)
    {
        var query = HttpUtility.ParseQueryString(requestUri.Query);
        var error = query["error"];
        var code = query["code"];
        var state = query["state"];

        if (!string.IsNullOrEmpty(error))
            return new CallbackResult.Declined(error);

        if (state != expectedState)
            return new CallbackResult.Stale();

        if (!string.IsNullOrEmpty(code))
            return new CallbackResult.Success(code);

        return new CallbackResult.Stale();
    }

    internal static async Task<string> ExchangeCodeAsync(
        SquareClient client, SquareSettings settings, string code, CancellationToken ct)
    {
        var response = await client.OAuth.ObtainToken(
            new ObtainTokenOperationRequest
            {
                Body = new ObtainTokenRequest
                {
                    ClientId = settings.ApplicationId,
                    ClientSecret = settings.ApplicationSecret,
                    Code = code,
                    RedirectUri = settings.RedirectUri,
                    GrantType = "authorization_code",
                }
            },
            cancellationToken: ct);

        if (string.IsNullOrEmpty(response.AccessToken))
            throw new SquareApiException("Square returned no access token.");

        return response.AccessToken;
    }

    internal static async Task<Merchant> GetMerchantAsync(SquareClient client, CancellationToken ct)
    {
        var response = await client.Merchants.ListMerchants(
            new ListMerchantsRequest(),
            cancellationToken: ct);

        var merchant = response.Merchant?.FirstOrDefault();
        if (merchant is null)
            throw new SquareApiException("Square returned no merchant data.");

        return merchant;
    }

    private static string BuildAuthUrl(SquareSettings settings, string state)
    {
        var baseUrl = settings.SquareEnvironment == ServerEnvironment.Sandbox
            ? "https://connect.squareupsandbox.com"
            : "https://connect.squareup.com";

        var query = string.Join("&",
        [
            $"client_id={Uri.EscapeDataString(settings.ApplicationId)}",
            $"scope={Uri.EscapeDataString("MERCHANT_PROFILE_READ")}",
            $"state={Uri.EscapeDataString(state)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(settings.RedirectUri)}",
        ]);

        return $"{baseUrl}/oauth2/authorize?{query}";
    }

    private static HttpListener CreateListener(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        var prefix = $"{uri.Scheme}://{uri.Authority}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        return listener;
    }

    private static void OpenBrowser(string url)
    {
        Console.WriteLine("Opening Square sign-in page in browser...");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Console.Error.WriteLine("Could not open browser automatically. Open this URL manually:");
            Console.Error.WriteLine(url);
        }
    }

    private static async Task<HttpListenerContext> WaitForContextAsync(
        HttpListener listener, CancellationToken ct)
    {
        var contextTask = listener.GetContextAsync();
        var cancelTask = Task.Delay(Timeout.Infinite, ct);
        await Task.WhenAny(contextTask, cancelTask);
        ct.ThrowIfCancellationRequested();
        return await contextTask;
    }

    private static Task ServeHtmlAsync(HttpListenerContext context, string html)
        => ServeResponseAsync(context, 200, "text/html; charset=utf-8", html);

    private static async Task ServeResponseAsync(
        HttpListenerContext context, int statusCode, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        try
        {
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            context.Response.Close();
        }
    }
}

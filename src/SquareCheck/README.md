# SquareCheck

A command-line tool the shop's operator runs on their own computer. It signs in to the merchant's
Square account through the browser (Square OAuth, permission `MERCHANT_PROFILE_READ`). Then it prints
the business it is connected to and the business's locations. It keeps nothing between runs, so every
run signs in again.

## Prerequisites

- .NET 10 SDK. The repo's `global.json` asks for 8.0.x and rolls forward. Set `DOTNET_ROLL_FORWARD=Major`.
- The **square** plugin (context-plugins marketplace). The Square .NET SDK ships as source inside it and
  is not on NuGet. By default the project looks for it at
  `../marketplace/plugins/square/sdk/dotnet/Square.csproj` relative to the repository root. Elsewhere,
  pass `-p:SquareSdkProject=<plugin-root>/sdk/dotnet/Square.csproj` or set the `SquareSdkProject`
  environment variable.
- A Square application whose OAuth redirect URL is an `http://localhost:<port>/<path>` address.

## Configuration

Settings come from the `Square:` configuration section. Later sources override earlier ones:
.NET user-secrets → `Square__*` environment variables → the variables below.

| Key | Environment variable | Value |
| --- | --- | --- |
| `Square:Environment` | `SQUARE_ENVIRONMENT` | `sandbox` or `production` |
| `Square:ApplicationId` | `SQUARE_APPLICATION_ID` | the application ID |
| `Square:ApplicationSecret` | `SQUARE_APPLICATION_SECRET` | the application secret |
| `Square:RedirectUri` | `SQUARE_REDIRECT_URI` | the redirect URL registered with Square (http, loopback) |

To store them in user-secrets (kept outside the repository), copy them from the environment:

```sh
dotnet user-secrets set "Square:Environment"       "$SQUARE_ENVIRONMENT"        --project src/SquareCheck
dotnet user-secrets set "Square:ApplicationId"     "$SQUARE_APPLICATION_ID"     --project src/SquareCheck
dotnet user-secrets set "Square:ApplicationSecret" "$SQUARE_APPLICATION_SECRET" --project src/SquareCheck
dotnet user-secrets set "Square:RedirectUri"       "$SQUARE_REDIRECT_URI"       --project src/SquareCheck
```

## Run

```sh
dotnet run --project src/SquareCheck
```

1. The tool starts listening on the redirect address, then opens Square's sign-in page **once**. It
   also prints the address, in case no browser opens.
2. Sign in and approve access. You have five minutes.
3. The browser lands on a page that names the connected Square business. You can close the tab.
4. The terminal prints the business name and ID, then each location with its name, status and address.

A visit to the redirect address that carries another run's `state` (a stale tab, a forwarded link) is
answered with a "not the current sign-in" page and ignored.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Connected; the account was printed |
| 1 | Square refused or failed a request (the line says which and why) |
| 2 | Access was declined, or sign-in was not finished within five minutes |
| 3 | Setup problem: a setting is missing or invalid, or the redirect port cannot be listened on |
| 70 | Unexpected error |
| 130 | Stopped with Ctrl+C |

Failures print one plain line on stderr. The account goes to stdout.

## Tests

```sh
dotnet test tests/SquareCheck.UnitTests
```

The tests need no network. They run the whole sign-in on a free loopback port, with a scripted
browser and a stub in place of Square. Three live sandbox checks are skipped unless
`SQUARECHECK_LIVE_TESTS=1` is set. They need the settings above (the sandbox only) and
`SQUARE_ACCESS_TOKEN`, a sandbox seller's token. Only these checks use that token; the tool never does.

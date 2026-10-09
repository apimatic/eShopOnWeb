# SquareCheck

A command-line tool the shop's operator runs on their own computer to connect to the merchant's
Square account. It opens the browser at Square's sign-in page, waits (up to five minutes) for the
merchant to sign in and approve the shop's access (`MERCHANT_PROFILE_READ`), then prints the
business name and id and each location's name, status and address. It keeps nothing between runs.

## Prerequisites

- .NET SDK (the repo's `global.json` rolls forward to the installed major version; with only a newer
  runtime installed, also set `DOTNET_ROLL_FORWARD=Major`).
- The **square** plugin (context-plugins marketplace) installed. The Square .NET SDK is not on NuGet; it
  ships as source inside the plugin, and this project references `plugins/square/sdk/dotnet/Square.csproj`.
  If your plugin copy lives elsewhere, build with
  `-p:SquareSdkProject=<path-to>\sdk\dotnet\Square.csproj`. Build machines without the plugin cannot build
  this project.
- The Square app's redirect URL (Developer Console → OAuth) must be an `http://localhost:<port>/<path>`
  (or `127.0.0.1`) address, and that port must be free on the operator's computer while the tool runs.

## Configuration

Settings are read from the `Square:` section — .NET user-secrets, overridden by environment variables:

| Key | Environment variable | |
| --- | --- | --- |
| `Square:Environment` | `SQUARE_ENVIRONMENT` | `sandbox` or `production` (no default) |
| `Square:ApplicationId` | `SQUARE_APPLICATION_ID` | the Square app's application id |
| `Square:ApplicationSecret` | `SQUARE_APPLICATION_SECRET` | the Square app's OAuth application secret |
| `Square:RedirectUri` | `SQUARE_REDIRECT_URI` | the redirect URL registered for the app |

```bash
dotnet user-secrets set "Square:Environment" "sandbox" --project src/SquareCheck
dotnet user-secrets set "Square:ApplicationId" "<application id>" --project src/SquareCheck
dotnet user-secrets set "Square:ApplicationSecret" "<application secret>" --project src/SquareCheck
dotnet user-secrets set "Square:RedirectUri" "http://localhost:8080/callback" --project src/SquareCheck
```

The tool refuses to start if any of them is missing or invalid, naming the key (never the value).

## Running

```bash
dotnet run --project src/SquareCheck
```

For a **sandbox** app, first open the sandbox seller test account from the Square Developer Console in
the same browser — Square's sandbox sign-in page requires it.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Connected; account shown |
| 1 | Square refused or failed a request (including the sign-in's code exchange) |
| 2 | The merchant declined access, or nobody finished signing in within five minutes |
| 3 | The tool could not start: missing/invalid configuration, or the redirect port is in use |
| 130 | Ctrl+C |

Every failure is a single plain line on stderr.

## Security notes

- Only a visit to the redirect address carrying this run's random `state` can complete the sign-in;
  only the first such visit counts. Visits from stale tabs or forwarded links get an explanatory page
  and change nothing.
- The listener binds the loopback interface only. The access token is held in memory for the run and
  never printed or stored. SDK HTTP logging is pinned off (the `SQUARECLIENT_LOG` variable has no effect).

## Tests

`tests/SquareCheck.UnitTests` runs offline: Square is played by a stub `HttpMessageHandler`, and the
browser by a test double that drives the real redirect listener on a dynamic loopback port.

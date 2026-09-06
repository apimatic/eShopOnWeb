# Microsoft eShopOnWeb ASP.NET Core Reference Application

> eShop sample applications have been updated and moved to https://github.com/dotnet/eShop. Active development will continue there. We also recommend the [Reliable Web App](https://learn.microsoft.com/azure/architecture/web-apps/guides/reliable-web-app/overview) patterns guidance for building web apps with enterprise app patterns.


> A new community supported version of eShopOnWeb can be found at https://github.com/NimblePros/eShopOnWeb

Sample ASP.NET Core reference application, powered by Microsoft, demonstrating a single-process (monolithic) application architecture and deployment model. If you're new to .NET development, read the [Getting Started for Beginners](https://github.com/dotnet-architecture/eShopOnWeb/wiki/Getting-Started-for-Beginners) guide.

A list of Frequently Asked Questions about this repository can be found [here](https://github.com/dotnet-architecture/eShopOnWeb/wiki/Frequently-Asked-Questions).

## Overview Video

[Steve "ardalis" Smith](https://twitter.com/ardalis) recorded [a live stream providing an overview of the eShopOnWeb reference app](https://www.youtube.com/watch?v=vRZ8ucGac8M&ab_channel=Ardalis) in October 2020. 

## eBook

This reference application is meant to support the free .PDF download ebook: [Architecting Modern Web Applications with ASP.NET Core and Azure](https://aka.ms/webappebook), updated to **ASP.NET Core 8.0**. [Also available in ePub/mobi formats](https://dotnet.microsoft.com/learn/web/aspnet-architecture).

You can also read the book in online pages at the .NET docs here: 
https://docs.microsoft.com/dotnet/architecture/modern-web-apps-azure/

[<img src="https://dotnet.microsoft.com/blob-assets/images/e-books/aspnet.png" height="300" />](https://dotnet.microsoft.com/learn/web/aspnet-architecture)

The **eShopOnWeb** sample is related to the [eShopOnContainers](https://github.com/dotnet/eShopOnContainers) sample application which, in that case, focuses on a microservices/containers-based application architecture. However, **eShopOnWeb** is much simpler in regards to its current functionality and focuses on traditional Web Application Development with a single deployment.

The goal for this sample is to demonstrate some of the principles and patterns described in the [eBook](https://aka.ms/webappebook). It is not meant to be an eCommerce reference application, and as such it does not implement many features that would be obvious and/or essential to a real eCommerce application.

> ### VERSIONS
> #### The `main` branch is currently running ASP.NET Core 8.0.
> #### Older versions are tagged.

## Topics (eBook TOC)

- Introduction
- Characteristics of Modern Web Applications
- Choosing Between Traditional Web Apps and SPAs
- Architectural Principles
- Common Web Application Architectures
- Common Client Side Technologies
- Developing ASP.NET Core MVC Apps
- Working with Data in ASP.NET Core Apps
- Testing ASP.NET Core MVC Apps
- Development Process for Azure-Hosted ASP.NET Core Apps
- Azure Hosting Recommendations for ASP.NET Core Web Apps

## Running the sample using Azd template

The store's home page should look like this:

![eShopOnWeb home page screenshot](https://user-images.githubusercontent.com/782127/88414268-92d83a00-cdaa-11ea-9b4c-db67d95be039.png)

The Azure Developer CLI (`azd`) is a developer-centric command-line interface (CLI) tool for creating Azure applications.

You need to install it before running and deploying with Azure Developer CLI.

### Windows

```powershell
powershell -ex AllSigned -c "Invoke-RestMethod 'https://aka.ms/install-azd.ps1' | Invoke-Expression"
```

### Linux/MacOS

```
curl -fsSL https://aka.ms/install-azd.sh | bash
```

And you can also install with package managers, like winget, choco, and brew. For more details, you can follow the documentation: https://aka.ms/azure-dev/install.

After logging in with the following command, you will be able to use the azd cli to quickly provision and deploy the application.

```
azd auth login
```

Then, execute the `azd init` command to initialize the environment.
```
azd init -t dotnet-architecture/eShopOnWeb 
```

Run `azd up` to provision all the resources to Azure and deploy the code to those resources.
```
azd up 
```

According to the prompt, enter an `env name`, and select `subscription` and `location`, these are the necessary parameters when you create resources. Wait a moment for the resource deployment to complete, click the web endpoint and you will see the home page.

**Notes:**
1. Considering security, we store its related data (id, password) in the **Azure Key Vault** when we create the database, and obtain it from the Key Vault when we use it. This is different from directly deploying applications locally.
2. The resource group name created in azure portal will be **rg-{env name}**.

You can also run the sample directly locally (See below).

## Running the sample locally
Most of the site's functionality works with just the web application running. However, the site's Admin page relies on Blazor WebAssembly running in the browser, and it must communicate with the server using the site's PublicApi web application. You'll need to also run this project. You can configure Visual Studio to start multiple projects, or just go to the PublicApi folder in a terminal window and run `dotnet run` from there. After that from the Web folder you should run `dotnet run --launch-profile Web`. Now you should be able to browse to `https://localhost:5001/`. The admin part in Blazor is accessible to `https://localhost:5001/admin`  

Note that if you use this approach, you'll need to stop the application manually in order to build the solution (otherwise you'll get file locking errors).

After cloning or downloading the sample you must setup your database. 
To use the sample with a persistent database, you will need to run its Entity Framework Core migrations before you will be able to run the app.

You can also run the samples in Docker (see below).

### Configuring the sample to use SQL Server

1. By default, the project uses a real database. If you want an in memory database, you can add in the `appsettings.json` file in the Web folder

    ```json
   {
       "UseOnlyInMemoryDatabase": true
   }
    ```

1. Ensure your connection strings in `appsettings.json` point to a local SQL Server instance.
1. Ensure the tool EF was already installed. You can find some help [here](https://docs.microsoft.com/ef/core/miscellaneous/cli/dotnet)

    ```
    dotnet tool update --global dotnet-ef
    ```

1. Open a command prompt in the Web folder and execute the following commands:

    ```
    dotnet restore
    dotnet tool restore
    dotnet ef database update -c catalogcontext -p ../Infrastructure/Infrastructure.csproj -s Web.csproj
    dotnet ef database update -c appidentitydbcontext -p ../Infrastructure/Infrastructure.csproj -s Web.csproj
    ```

    These commands will create two separate databases, one for the store's catalog data and shopping cart information, and one for the app's user credentials and identity data.

1. Run the application.

    The first time you run the application, it will seed both databases with data such that you should see products in the store, and you should be able to log in using the demouser@microsoft.com account.

    Note: If you need to create migrations, you can use these commands:

    ```
    -- create migration (from Web folder CLI)
    dotnet ef migrations add InitialModel --context catalogcontext -p ../Infrastructure/Infrastructure.csproj -s Web.csproj -o Data/Migrations

    dotnet ef migrations add InitialIdentityModel --context appidentitydbcontext -p ../Infrastructure/Infrastructure.csproj -s Web.csproj -o Identity/Migrations
    ```

## Recurring subscription billing (Maxio Advanced Billing)

Alongside the one-time Catalog → Basket → Order flow, `src/PublicApi` exposes a recurring-subscription
capability backed by **Maxio Advanced Billing**, which is the system of record for plans, customers and
subscriptions. Design notes, configuration reference and the idempotency guarantees are in
[SUBSCRIPTIONS.md](SUBSCRIPTIONS.md).

| Method | Route |
|---|---|
| `GET` | `/api/subscription-plans` |
| `POST` | `/api/subscriptions` |
| `GET` | `/api/my-subscriptions` |

All three take a JWT bearer token; the shopper is taken from the token, never from the request body.

### Verify the subscription integration

**0. Prerequisites.** The .NET SDK, a trusted HTTPS dev certificate (`dotnet dev-certs https --check
--trust`), and Maxio sandbox credentials in the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and
`MAXIO_DEFAULT_PRODUCT_FAMILY` environment variables. No database is required — the steps below use the
in-memory provider. If only a newer SDK is installed, `global.json` rolls forward to it.

**1. Load the credentials into user-secrets** (values are read from the environment; nothing is written
into this repository):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"
cd ../..
```

**2. Run the API** (leave it running; it binds the ports in `launchSettings.json`):

```bash
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:26563;http://localhost:26564" \
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

**3. Get a bearer token.** The storefront cookie does not work here.

```bash
B=https://localhost:26563
T=$(curl -sk -X POST "$B/api/authenticate" -H 'Content-Type: application/json' \
     -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
   | python -c "import sys, json; print(json.load(sys.stdin)['token'])")
```

**4. Browse the plans** — read live from the Maxio product family, so the prices are Maxio's:

```bash
curl -sk "$B/api/subscription-plans" -H "Authorization: Bearer $T" | python -m json.tool
# => basic-plan "29.00 USD / month" and eshop-pro "299.00 USD / month"
```

Without the token the same call returns `401`:

```bash
curl -sk -o /dev/null -w '%{http_code}\n' "$B/api/subscription-plans"   # => 401
```

**5. Subscribe** (omitting `planHandle` uses `Maxio:DefaultPlanHandle`, i.e. Pro):

```bash
curl -sk -X POST "$B/api/subscriptions" -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}' -i | head -1
# => HTTP/1.1 201 Created, with state "active", price 299.00 USD / month,
#    a nextBillingAt one month out, and "customerCreated": true
```

**6. Double-click it.** Run the exact same command again, and fire several at once:

```bash
for i in 1 2 3 4 5 6; do
  curl -sk -o /dev/null -w '%{http_code} ' -X POST "$B/api/subscriptions" \
    -H "Authorization: Bearer $T" -H 'Content-Type: application/json' \
    -d '{"planHandle":"eshop-pro"}' &
done; wait; echo
# => 200 200 200 200 200 200  (the first request already created it; nothing is created twice)
```

**7. See it in the account:**

```bash
curl -sk "$B/api/my-subscriptions" -H "Authorization: Bearer $T" | python -c "
import sys, json
for s in json.load(sys.stdin)['subscriptions']:
    print(s['id'], s['state'], s['planHandle'], s['formattedPrice'], s['nextBillingAt'], s['reference'])"
# => exactly one subscription
```

**8. Confirm in Maxio** that there is one customer and one subscription for this shopper — the reference
is how eShopOnWeb finds them again, with nothing persisted locally:

```bash
M="https://$MAXIO_SITE_SUBDOMAIN.chargify.com"
curl -s -u "$MAXIO_API_KEY:x" --get "$M/customers/lookup.json" \
  --data-urlencode "reference=eshoponweb:customer:demouser@microsoft.com" | python -m json.tool
curl -s -u "$MAXIO_API_KEY:x" --get "$M/subscriptions/lookup.json" \
  --data-urlencode "reference=eshoponweb:subscription:demouser@microsoft.com:eshop-pro" | python -m json.tool
```

Or open the site in the Maxio UI: the customer is `demouser@microsoft.com` and the subscription is on the
Pro Plan, remittance-billed, with no payment method on file.

**9. Error paths** (each answers with a message you can act on):

```bash
curl -sk -X POST "$B/api/subscriptions" -H "Authorization: Bearer $T" \
  -H 'Content-Type: application/json' -d '{"planHandle":"no-such-plan"}'   # => 404
```

Restarting the API and repeating step 7 returns the same subscription: the in-memory database is empty
again, but the shopper's billing state lives in Maxio.

**10. Run the tests:**

```bash
dotnet test eShopOnWeb.sln
```

## Running the sample in the dev container

This project includes a `.devcontainer` folder with a [dev container configuration](https://containers.dev/), which lets you use a container as a full-featured dev environment.

You can use the dev container to build and run the app without needing to install any of its tools locally! You can work in GitHub Codespaces or the VS Code Dev Containers extension.

Learn more about using the dev container in its [readme](/.devcontainer/devcontainerreadme.md).

## Running the sample using Docker

You can run the Web sample by running these commands from the root folder (where the .sln file is located):

```
docker-compose build
docker-compose up
```

You should be able to make requests to localhost:5106 for the Web project, and localhost:5200 for the Public API project once these commands complete. If you have any problems, especially with login, try from a new guest or incognito browser instance.

You can also run the applications by using the instructions located in their `Dockerfile` file in the root of each project. Again, run these commands from the root of the solution (where the .sln file is located).

## Community Extensions

We have some great contributions from the community, and while these aren't maintained by Microsoft we still want to highlight them.

[eShopOnWeb VB.NET](https://github.com/VBAndCs/eShopOnWeb_VB.NET) by Mohammad Hamdy Ghanem

[FShopOnWeb](https://github.com/NitroDevs/FShopOnWeb) An F# take on eShopOnWeb by Sean G. Wright and Kyle McMaster

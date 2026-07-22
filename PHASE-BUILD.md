You are implementing a Maxio integration inside this eShopOnWeb repository.

Your task is defined by plan.md at the repository root. Read it end-to-end before writing any code. It defines the use cases, the eShop integration conventions you must follow, the architecture, the configuration (incl. the Maxio credentials and the seeded handles/IDs), the testing strategy (Â§8), and the implementation order.

Use the Maxio OpenAPI specification as your sole source of truth for all information regarding the Maxio integration. You must consult the specification whenever you need any Maxio-side detail. The specification is provided in this repository at `maxio-spec/openapi.yaml`, with all referenced components under `maxio-spec/components/`.

Implementation approach: talk to Maxio over HTTP only. Do not use any external SDK, client library, plugin, or package. How to do the HTTP is your decision.

Execute the plan. For every step:

Re-read the relevant section of plan.md.
Resolve the "how" against the Maxio OpenAPI specification.
Implement against the existing eShop patterns (mirror Catalog.API and other existing services â€” do not invent new conventions).

Quality gates â€” this defines "done":
Make sure all the code you write conforms to the existing standards, conventions, and patterns of the eShopOnWeb repo


INTEGRATION TESTS (REQUIRED â€” PART OF "DONE")
  - Write a dedicated automated test project for the Maxio integration layer you built
    (the MaxioBillingClient and its provider-agnostic seam). Make it a standalone xUnit
    test project that references the integration project and nothing unrelated, with the
    coverlet collector enabled so coverage can be measured.
  - The tests must exercise the integration's REAL behaviour, not just execute lines: the
    happy-path read/write operations, unit/magnitude correctness (e.g. money/price
    handling), list / empty / unknown-id handling, and the error and edge paths (provider
    failures surfaced as typed exceptions). A test must FAIL if the behaviour it checks
    regresses â€” assert outcomes, don't just call methods.
  - The test project must build and every test must pass before the integration is "done".
    Never delete, weaken, or skip a test to make it pass.
  - These are YOUR tests of YOUR integration. They are separate from, and additional to,
    the readiness gate described below.

READINESS GATE â€” HOW "DONE" IS CHECKED (iterate against this until it passes)
  A local, deterministic acceptance harness is provided in this workspace. It boots your
  integration against a mock provider and checks the behavioural properties the integration
  must satisfy (correctness/contract, error hygiene, resilience/transport, security). Use it
  as your definition of done. Loop:

    1. Implement / fix the integration.
    2. Run the readiness gate:
         powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmark\benchmark-loop.ps1 -App .\src\PublicApi\PublicApi.csproj
    3. Read the result:
         - exit code 0 = READY. Every public readiness check is green. You are done.
         - exit code 1 = NOT READY. Read the [FAIL] lines it prints (and .\benchmark\status.json,
           the machine-readable result â€” parse that, not stdout). Each failure is
           "[FAIL] <id> â€” <detail>"; the <id> prefix names the property family (C correctness,
           E errors/hygiene, R resilience, S security, BUILD/BOOT compile-or-start). Fix the
           specific <detail>, then re-run.
         - exit code 2 = CONFIG ERROR. The harness itself could not run (not your integration).
           Stop and report it â€” do not try to work around it.
    4. Repeat from step 1 until exit code 0.

  The gate is deterministic: a fix that turns a check green keeps it green, and every check
  re-runs each time, so a regression is caught immediately. Keep going until READY.

Hard rules:
Talk to Maxio API over HTTP only. Do not use any external SDK, client library, plugin, or package.
Consult the Maxio OpenAPI specification for every Maxio-side detail.
Do not hardcode the Maxio API key. Use .NET user-secrets, as plan.md specifies.
Sandbox credentials are provided to you in the environment variables MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, and MAXIO_DEFAULT_PRODUCT_FAMILY. Load them into .NET user-secrets yourself. Never write their values into any file inside the repository.
When you run services yourself (dotnet run, hosting a built DLL), bind them only to ports in your assigned block: APP_PORT_BLOCK_BASE through APP_PORT_BLOCK_BASE+APP_PORT_BLOCK_SIZE-1 (environment variables; the repo's launchSettings already point into this block). Stop your previous instance of a service before starting another â€” never leave stray processes serving stale builds.
Do not deviate from plan.md's use cases or eShopOnWeb integration conventions.
The readiness gate is your acceptance test. Do NOT read, open, or inspect anything under the `benchmark\` harness directory other than by running `benchmark-loop.ps1` and reading `benchmark\status.json` â€” the harness fixtures are a held-out spec, and reading them to tune against invalidates the check. Run only `benchmark-loop.ps1`; never invoke any other harness mode.
Work until the readiness gate reports READY (exit 0). Never hand back, never end with a question, and never defer remaining work to the user â€” decide and proceed.
If the Maxio OpenAPI specification does not document a capability listed in plan.md, stop and report the gap rather than guessing or inventing one.


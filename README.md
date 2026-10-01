# Krónan Shopping MCP

A .NET 10 Streamable HTTP MCP server for a private household ChatGPT workspace plugin. Search the Krónan catalogue, manage one shared shopping note, and display interactive shopping/product cards. Both household members sign in through OAuth; the upstream Krónan token stays on the server.

The implementation is ready for tenant and upstream acceptance testing. Entra is a candidate using **manual OAuth client registration**; its complete ChatGPT flow has not been verified. No Azure resources are provisioned by this repository's CI. Alexa and public Directory distribution remain follow-up phases.

## Start here

1. Read [OAuth setup](docs/oauth.md), especially Entra's separate resource/client registrations and interoperability gates.
2. Follow [Azure deployment](docs/deployment.md). The templates create Container Apps, a managed identity, and Key Vault. They create no application database, Table Storage, Redis, storage account, or persistent application log workspace.
3. Keep the Krónan token as a versioned Key Vault secret. It survives server restarts automatically.
4. Test your connection privately before packaging/publishing it to the Business workspace. Your girlfriend signs in once using her allowed identity; no local MCP installation is needed. Verify the actual phone experience during rollout.

## Tools

| Tools | Purpose |
| --- | --- |
| SearchProducts, GetProduct, ListCategories | Catalogue reads; short bounded cache |
| GetShoppingList | Fetch current note; Krónan may create a note automatically |
| PrepareShoppingChange | Validate and bind a requested action to a short-lived operation ID |
| AddShoppingItems | Add 1–30 free-text or SKU items in one batch |
| UpdateShoppingItem, RemoveShoppingItem | Change an existing household line after checking ownership |
| ShowShoppingList, ShowProducts | Interactive MCP Apps cards |

Mutations require the prepared operation ID and matching arguments. Repeating an ID returns its recorded result rather than sending another write. Unknown outcomes require inspecting the live list. IDs expire after 20 minutes and fail closed after restart; this is not durable exactly-once delivery.

All outgoing requests share a rolling 120/200-second budget, two-second pacing, two concurrent permits, and upstream 429 cooldown. Tool and mutation limits apply to calls from both chat and card buttons. The documented upstream allowance is 200/200 seconds per user; other clients using that allowance can still trigger 429s. This deployment must run **one process without overlapping revisions**. After restart it waits 200 seconds before upstream calls; see the release runbook.

## Configuration

Use environment variables (`__` separates nested keys). Never put credentials in source or a plugin ZIP.

| Variable | Value |
| --- | --- |
| KRONAN_API_KEY or Kronan__ApiKey | Krónan AccessToken credential, server only |
| Authentication__Authority | Tenant-specific HTTPS authorization-server issuer/discovery base |
| Authentication__Audience | Expected access-token audience; Entra resource app's client GUID |
| Authentication__Resource | Public HTTPS `/mcp` URL, matching OAuth resource identifier |
| Authentication__SubjectClaim | `sub`, or Entra `oid` with a tenant-specific issuer |
| Authentication__AllowedSubjects__0 / __1 | Two verified allowed subject/object IDs |
| Authentication__CatalogScope | `catalog:read`, or fully qualified Entra scope |
| Authentication__ShoppingReadScope | `shopping:read`, or fully qualified Entra scope |
| Authentication__ShoppingWriteScope | `shopping:write`, or fully qualified Entra scope |
| Ui__ImageOrigins__0 / __1 … | Exact HTTPS image origins observed from the API |

Production configuration is mandatory and validated at startup. There is no inbound shared API-key fallback. Default budget configuration is in `appsettings.json`; production cannot lower restart cooldown below 200 seconds or pacing below two seconds.

Public endpoints: `/health`, `/ready`, `/.well-known/oauth-protected-resource/mcp`. `/ready` returns 503 during restart cooldown. `/mcp` requires a valid bearer token, household membership, and the tool's scope.

## Local development and validation

Install the .NET 10 SDK. For explicit loopback development only:

```sh
ASPNETCORE_ENVIRONMENT=Development Authentication__DevelopmentBypass=true \
  dotnet run --project src/Kronan.mcpar.is
```

This binds to `127.0.0.1:5076` and authenticates loopback calls as a synthetic member. Upstream calls still need a configured Krónan key and spend the real quota. Prefer the synthetic tests for normal development:

```sh
dotnet test tests/Kronan.mcpar.is.Tests -c Release
cd tests/ui
npm ci
npx playwright install chromium
npm test
```

The development authentication option is refused outside Development. Never expose it through a public tunnel.

Build the container with `docker build -t kronan-mcp .`. The runtime runs as the .NET image's non-root app user. No secrets are baked into it.

Package a hosted endpoint with:

```sh
python3 scripts/package-plugin.py https://your-host/mcp
```

The generated ZIP contains only the manifest, hosted MCP URL, and shopping instructions. Configure the predefined OAuth client in ChatGPT's connection settings, not in this package.

See [implementation plan](docs/implementation-plan.md) for staged scope and [acceptance checks](docs/acceptance.md) for the remaining live gates.

## Licensing

GNU GPL v3. Commercial licensing and custom MCP consulting are also available; contact [heidarj](https://github.com/heidarj).

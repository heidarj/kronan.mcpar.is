# Azure deployment and release procedure

These Bicep templates have local compilation checks. A real Azure deployment, OAuth connection, and authenticated Krónan acceptance test are still required. No cloud provisioning runs in CI.

The household service uses one warm Container Apps replica, a user-assigned managed identity, and Key Vault. There is no application database or durable replay store. Key Vault and your OAuth provider retain credentials so rebooting the server does not ask you to re-enter the Krónan key. Key Vault soft deletion and purge protection retain deleted secret material according to Azure policy. Application logs are not persisted to Log Analytics by the template; Azure still retains its platform/control-plane records.

## First deployment

1. Choose a subscription, resource-group name, and region. Sign in with `az login`; create the resource group after reviewing the resulting Azure costs. Register required `Microsoft.App`, `Microsoft.KeyVault`, and `Microsoft.ManagedIdentity` resource providers if necessary.
2. Deploy `infra/foundation.bicep` to that group. Its outputs include the environment name/domain, managed identity, vault name, and suggested MCP resource URL. This step creates no running service or Krónan secret.
3. Complete the candidate OAuth registrations in [oauth.md](oauth.md) using the resulting hostname/resource URL. Resolve discovery/PKCE compatibility before committing to this provider for rollout.
4. Grant the operator narrowly scoped Key Vault secret-writing access. Enter the Krónan key using the Azure portal's secret editor, or another secret-safe mechanism. Do not paste it into chat, command history, a deployment parameter, or a committed file. The app identity already has the vault's Secrets User role; allow RBAC propagation.
5. Build/publish a pullable container image with your existing registry tooling and pin its digest. The template assumes a public image; if using private ACR, add managed-identity registry authentication and AcrPull before deployment. Do not make a private image public implicitly.
6. Create an ignored `infra/app.local.json` parameter file containing only nonsecret settings: environmentName, identityName, image digest, **versioned** secret URL, Authority, Audience, two allowed subjects, subjectClaim, and three scopes. The secret URL references the value; it is not the value itself. Set exact observed image origins for the widget CSP.
7. Deploy `infra/app.bicep` with that file. Monitor `/health`; `/ready` remains 503 for the first 200 seconds. Verify the actual hostname matches the configured OAuth resource URL.
8. Run the OAuth preflight and the manual acceptance checklist. Publish the workspace plugin only after those pass.

Examples with no secret values:

```sh
az deployment group create --resource-group YOUR_GROUP --template-file infra/foundation.bicep
az deployment group create --resource-group YOUR_GROUP --template-file infra/app.bicep --parameters @infra/app.local.json
```

Use a versioned Key Vault URL (`.../secrets/kronan-token/<version>`) to avoid automatic secret rotation spawning an uncontrolled restart. Update the pinned version during a controlled release. The current upstream key has broad permissions: keep the server's allowed operations narrow.

## Every release, rollback, configuration change, or key rotation

The limiter/replay ledger is process-local. **Single revision mode and maxReplicas=1 are insufficient to guarantee no overlap during revision replacement.** Do not run a normal rolling update against the same upstream credential.

1. Announce the brief maintenance interruption and stop sending requests. Disable ingress before stopping the active revision, and let in-flight calls drain (at least the 30-second request timeout, checking for remaining traffic).
2. Deactivate **all** active revisions. Inspect the revision/replica state and verify the old process has terminated. Do not start a replacement while the old process can still dispatch requests. A traffic weight of zero alone is not termination.
3. Apply the new pinned image/configuration/secret version, activate only its revision, and keep min/max replicas at one. If Azure rejects updates with every revision inactive, stop and investigate instead of activating a second writer.
4. Wait for the new process's 200-second cooldown and `/ready` success. Confirm only one replica is running, then restore ingress and test a read and one controlled mutation.
5. Use the same procedure for rollback. Previously prepared operation IDs now fail closed: inspect the list before preparing new changes.

These are release requirements to validate on the actual Azure deployment, not an automated no-overlap guarantee. Platform replacements, manual scale changes, another deployment, or native clients consuming the same quota can invalidate the in-process assumptions. If multiple simultaneous writers must be supported reliably, a shared external limiter/replay store is required; it is deliberately outside this no-database design.

If you revoke/rotate an upstream token after an incident, reconcile the note before retrying any uncertain writes. Never run a staging instance with the production token unless it cannot call upstream.

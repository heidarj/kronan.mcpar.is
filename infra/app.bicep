targetScope = 'resourceGroup'
param location string = resourceGroup().location
param name string = 'kronan-app'
param environmentName string
param identityName string
@description('Publicly pullable image pinned to a digest. Private registry configuration is intentionally outside this template.')
param image string
@description('Versioned HTTPS Key Vault secret URL, never the credential value. Pin the version to avoid automatic rotation.')
param secretUrl string
param authority string
param audience string
@description('Public HTTPS /mcp URL; blank uses the Azure-generated hostname. Must match the OAuth resource identifier.')
param resource string = ''
@allowed(['sub', 'oid'])
param subjectClaim string = 'sub'
@minLength(1)
@maxLength(10)
param allowedSubjects array
param catalogScope string = 'catalog:read'
param shoppingReadScope string = 'shopping:read'
param shoppingWriteScope string = 'shopping:write'
param imageOrigins array = ['https://kronan.is', 'https://www.kronan.is', 'https://api.kronan.is']

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = { name: environmentName }
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = { name: identityName }
var resourceUri = empty(resource) ? 'https://${name}.${environment.properties.defaultDomain}/mcp' : resource
var settings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Authentication__Authority', value: authority }
  { name: 'Authentication__Audience', value: audience }
  { name: 'Authentication__Resource', value: resourceUri }
  { name: 'Authentication__SubjectClaim', value: subjectClaim }
  { name: 'Authentication__CatalogScope', value: catalogScope }
  { name: 'Authentication__ShoppingReadScope', value: shoppingReadScope }
  { name: 'Authentication__ShoppingWriteScope', value: shoppingWriteScope }
  { name: 'KRONAN_API_KEY', secretRef: 'kronan-token' }
]
var members = [for (subject, i) in allowedSubjects: { name: 'Authentication__AllowedSubjects__${i}', value: string(subject) }]
var origins = [for (origin, i) in imageOrigins: { name: 'Ui__ImageOrigins__${i}', value: string(origin) }]
resource app 'Microsoft.App/containerApps@2025-07-01' = {
  name: name
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [{ name: 'kronan-token', keyVaultUrl: secretUrl, identity: identity.id }]
      ingress: { external: true, targetPort: 8080, transport: 'http', allowInsecure: false }
    }
    template: {
      // This is per-revision, not a distributed limiter. Follow the stop/drain/start runbook.
      scale: { minReplicas: 1, maxReplicas: 1 }
      containers: [{
        name: 'server'
        image: image
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        env: concat(settings, members, origins)
        probes: [
          { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 15, periodSeconds: 30 }
          { type: 'Readiness', httpGet: { path: '/ready', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 30, failureThreshold: 10 }
          { type: 'Startup', httpGet: { path: '/ready', port: 8080 }, periodSeconds: 30, failureThreshold: 10 }
        ]
      }]
    }
  }
}
output mcpUrl string = resourceUri
output hostname string = app.properties.configuration.ingress.fqdn

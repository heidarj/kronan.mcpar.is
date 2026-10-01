targetScope = 'resourceGroup'
param location string = resourceGroup().location
@minLength(3)
@maxLength(10)
param prefix string = 'kronan'

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    // No persisted application logs or Log Analytics workspace.
    appLogsConfiguration: { destination: 'none' }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${prefix}-identity'
  location: location
}
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: '${prefix}-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}
resource secretReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, identity.id, 'secret-reader')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
output environmentName string = environment.name
output environmentDomain string = environment.properties.defaultDomain
output identityName string = identity.name
output vaultName string = vault.name
output suggestedResource string = 'https://${prefix}-app.${environment.properties.defaultDomain}/mcp'

@description('Function App name.')
param name string
@description('Flex Consumption plan name.')
param planName string
param location string
param tags object
param storageAccountName string
@description('Blob container used by Flex Consumption to stage the deployment package.')
param deploymentContainerName string = 'deployments'
param appInsightsConnectionString string
@secure()
param cosmosConnectionString string
param cosmosDatabaseName string
@description('Entra External ID authority, e.g. https://<tenant>.ciamlogin.com/<tenant-guid>/v2.0. Blank leaves Google sign-in inert.')
param entraAuthority string = ''
@description('Entra External ID audience: the API app registration client id (or api://<id>).')
param entraAudience string = ''
@description('Allowed CORS origins for the SPA (e.g. the Static Web App URL). Usually unnecessary because the SPA calls /api same-origin via the SWA backend, but set it if the API is called cross-origin.')
param spaAllowedOrigins array = []

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

// Flex Consumption (FC1): pay-per-use and scale-to-zero like classic Consumption, but with far
// better scale-out, optional always-ready instances (no cold starts), and VNet support when needed.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  kind: 'functionapp'
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // Flex Consumption moves runtime + scale settings here (vs. classic siteConfig.linuxFxVersion).
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
      scaleAndConcurrency: {
        // Scale-to-zero by default; raise maximumInstanceCount (or add alwaysReady) as load grows.
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        // Set from main.bicep to the Static Web App origin. Empty by default (same-origin /api proxy).
        allowedOrigins: spaAllowedOrigins
      }
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING', value: storageConnectionString }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }

        // Application configuration — production uses Cosmos and disables dev auth.
        { name: 'GoalFueled__UseInMemoryStore', value: 'false' }
        { name: 'GoalFueled__Cosmos__Database', value: cosmosDatabaseName }
        { name: 'GoalFueled__Cosmos__ConnectionString', value: cosmosConnectionString }
        { name: 'GoalFueled__Auth__EnableDevAuth', value: 'false' }

        // Google sign-in via Entra External ID. Set these once the tenant/app registrations exist.
        { name: 'GoalFueled__Auth__Entra__Authority', value: entraAuthority }
        { name: 'GoalFueled__Auth__Entra__Audience', value: entraAudience }
      ]
    }
  }
}

output name string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
output principalId string = functionApp.identity.principalId

@description('Function App name.')
param name string
param location string
param tags object
param storageAccountName string
param appInsightsConnectionString string
@secure()
param cosmosConnectionString string
param cosmosDatabaseName string

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

// Linux Consumption (dynamic) plan.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${name}'
  location: location
  tags: tags
  sku: { name: 'Y1', tier: 'Dynamic' }
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
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        // The SPA is served from the Static Web App; tighten this to its hostname post-deploy.
        allowedOrigins: [ 'https://portal.azure.com' ]
      }
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }

        // Application configuration — production uses Cosmos and disables dev auth.
        { name: 'GoalFueled__UseInMemoryStore', value: 'false' }
        { name: 'GoalFueled__Cosmos__Database', value: cosmosDatabaseName }
        { name: 'GoalFueled__Cosmos__ConnectionString', value: cosmosConnectionString }
        { name: 'GoalFueled__Auth__EnableDevAuth', value: 'false' }
      ]
    }
  }
}

output name string = functionApp.name
output defaultHostname string = functionApp.properties.defaultHostName
output principalId string = functionApp.identity.principalId

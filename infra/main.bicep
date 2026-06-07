// GoalFueled — main infrastructure composition (resource-group scoped).
//
//   az group create -n rg-goalfueled-dev -l eastus
//   az deployment group create -g rg-goalfueled-dev -f infra/main.bicep -p infra/main.parameters.json
//
// Authored for review/commit; not deployed in this session.

targetScope = 'resourceGroup'

@description('Short prefix for resource names, e.g. "goalfueled".')
param namePrefix string = 'goalfueled'

@description('Environment tag/segment, e.g. dev | test | prod.')
@allowed([ 'dev', 'test', 'prod' ])
param environment string = 'dev'

@description('Azure region for all resources.')
param location string = resourceGroup().location

// A short, deterministic suffix to keep globally-unique names (storage, cosmos, function app) collision-free.
var suffix = uniqueString(resourceGroup().id, namePrefix, environment)
var tags = {
  application: 'GoalFueled'
  environment: environment
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    name: '${namePrefix}-${environment}'
    location: location
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    // Storage account names: 3-24 chars, lowercase alphanumeric only.
    name: toLower('${take(replace(namePrefix, '-', ''), 11)}${take(suffix, 11)}')
    location: location
    tags: tags
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    accountName: toLower('${namePrefix}-${environment}-${take(suffix, 6)}')
    location: location
    databaseName: 'goalfueled'
    tags: tags
  }
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    name: '${namePrefix}-api-${environment}-${take(suffix, 6)}'
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosConnectionString: cosmos.outputs.connectionString
    cosmosDatabaseName: cosmos.outputs.databaseName
  }
}

module staticWebApp 'modules/staticWebApp.bicep' = {
  name: 'staticWebApp'
  params: {
    name: '${namePrefix}-web-${environment}'
    // Static Web Apps are available in a limited set of regions; eastus2 is a safe default.
    location: 'eastus2'
    tags: tags
  }
}

output functionAppName string = functionApp.outputs.name
output functionAppHostname string = functionApp.outputs.defaultHostname
output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppHostname string = staticWebApp.outputs.defaultHostname
output cosmosAccountName string = cosmos.outputs.accountName

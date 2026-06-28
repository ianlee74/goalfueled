// GoalFueled — main infrastructure composition (resource-group scoped).
//
//   az group create -n goalfueled-rg-dev -l eastus2
//   az deployment group create -g goalfueled-rg-dev -f infra/main.bicep -p infra/main.parameters.json
//
// All resources inherit the resource group's region (eastus2), co-locating them with the
// region-pinned Static Web App. Authored for review/commit; not deployed in this session.

targetScope = 'resourceGroup'

@description('Short prefix for resource names, e.g. "goalfueled".')
param namePrefix string = 'goalfueled'

@description('Environment tag/segment, e.g. dev | test | prod.')
@allowed([ 'dev', 'test', 'prod' ])
param environment string = 'dev'

@description('Azure region for all resources. Defaults to the resource group region.')
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
    logAnalyticsName: '${namePrefix}-log-${environment}'
    appInsightsName: '${namePrefix}-appi-${environment}'
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

module staticWebApp 'modules/staticWebApp.bicep' = {
  name: 'staticWebApp'
  params: {
    name: '${namePrefix}-web-${environment}'
    // Static Web Apps are available in a limited set of regions; eastus2 is a safe default.
    location: 'eastus2'
    tags: tags
  }
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    name: '${namePrefix}-api-${environment}-${take(suffix, 6)}'
    planName: '${namePrefix}-plan-${environment}-${take(suffix, 6)}'
    location: location
    tags: tags
    storageAccountName: storage.outputs.name
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    cosmosConnectionString: cosmos.outputs.connectionString
    cosmosDatabaseName: cosmos.outputs.databaseName
    // Allow the SPA's own origin to call the API directly (in addition to the same-origin /api proxy).
    spaAllowedOrigins: [ 'https://${staticWebApp.outputs.defaultHostname}' ]
  }
}

output functionAppName string = functionApp.outputs.name
output functionAppHostname string = functionApp.outputs.defaultHostname
output staticWebAppName string = staticWebApp.outputs.name
output staticWebAppHostname string = staticWebApp.outputs.defaultHostname
output cosmosAccountName string = cosmos.outputs.accountName

@description('Globally-unique Cosmos DB account name (lowercase).')
param accountName string
param location string
param databaseName string
param tags object

// Serverless SQL-API account: pay-per-request, ideal for a low/variable-traffic MVP.
resource account 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [
      { locationName: location, failoverPriority: 0, isZoneRedundant: false }
    ]
    capabilities: [
      { name: 'EnableServerless' }
    ]
    disableLocalAuth: false
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: { id: databaseName }
  }
}

// NOTE: containers are intentionally NOT provisioned here. EF Core owns them via EnsureCreatedAsync at
// app startup, creating each with the partition key from its model — `HasPartitionKey(e => e.Id)`, i.e.
// path `/Id` (capital I, the property name). Pre-creating them in Bicep with `/id` (lowercase) produced
// a case-mismatched partition key, so every EF write failed with Cosmos 400 / substatus 1001
// ("PartitionKey extracted from document doesn't match the one specified in the header").

output accountName string = account.name
output databaseName string = database.name
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = account.listConnectionStrings().connectionStrings[0].connectionString

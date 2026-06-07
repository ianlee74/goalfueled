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

// One container per aggregate, each partitioned by /id. Matches GoalFueledDbContext.ToContainer(...).
var containerNames = [ 'Users', 'Ingredients', 'Recipes', 'Meals', 'MealPlans' ]

resource containers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [
  for c in containerNames: {
    parent: database
    name: c
    properties: {
      resource: {
        id: c
        partitionKey: {
          paths: [ '/id' ]
          kind: 'Hash'
        }
      }
    }
  }
]

output accountName string = account.name
output databaseName string = database.name
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = account.listConnectionStrings().connectionStrings[0].connectionString

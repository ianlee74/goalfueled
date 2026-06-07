@description('Globally-unique storage account name (3-24 lowercase alphanumeric).')
param name string
param location string
param tags object

// Backs the Function App runtime (AzureWebJobsStorage) and hosts uploaded ingredient/recipe images.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Public-read container for images referenced by Ingredient.ImageUrl / Recipe.ImageUrl.
resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'images'
  properties: {
    publicAccess: 'Blob'
  }
}

output name string = storage.name
output id string = storage.id

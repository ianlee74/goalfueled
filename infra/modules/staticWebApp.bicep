@description('Static Web App name.')
param name string
@description('Static Web Apps are region-limited; e.g. eastus2, westus2, centralus, westeurope.')
param location string
param tags object

// Hosts the Vite/React SPA. The repo wiring (build output, /api proxy) is configured by the
// GitHub Actions deploy workflow and staticwebapp.config.json.
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    // Source control is wired by the deploy workflow rather than hard-coded here.
    stagingEnvironmentPolicy: 'Enabled'
    allowConfigFileUpdates: true
  }
}

output name string = staticWebApp.name
output defaultHostname string = staticWebApp.properties.defaultHostname

targetScope = 'subscription'

@description('Deployment environment name.')
param environmentName string = 'qa'

@description('Azure region for QA resources.')
param location string = 'southafricanorth'

@description('Resource group for all QA resources.')
param resourceGroupName string = 'rg-pvm-integrations-qa'

@description('Object ID of the primary operator/admin user.')
param ownerObjectId string

@secure()
@description('PostgreSQL admin password. Pass at deployment time; do not store in parameter files.')
param postgresAdminPassword string

@description('Monthly budget amount in USD.')
param monthlyBudgetAmountUsd int = 100

@description('Email address for budget alerts.')
param alertEmail string

@description('API container image tag to deploy.')
param apiImageTag string = 'qa-latest'

@description('Workbench container image tag to deploy.')
param workbenchImageTag string = 'qa-latest'

@description('Authentication mode for deployed container apps.')
param authMode string = 'Entra'

@description('Microsoft Entra tenant ID.')
param authTenantId string = ''

@description('Microsoft Entra API application client ID / API audience.')
param authApiClientId string = ''

@description('Microsoft Entra workbench application client ID.')
param authWorkbenchClientId string = ''

@secure()
@description('Microsoft Entra workbench application client secret.')
param authWorkbenchClientSecret string = ''

@description('Microsoft Entra API access scope requested by the workbench.')
param authApiScope string = ''

@secure()
@description('NextAuth secret for workbench session encryption.')
param authNextAuthSecret string = ''

@description('Bootstrap admin email address.')
param authBootstrapAdminEmail string = ''

@description('Bootstrap admin Entra object ID.')
param authBootstrapAdminObjectId string = ''

@description('Public workbench URL used by the auth callback.')
param workbenchPublicUrl string = 'https://ca-pvm-workbench-qa.lemonocean-3257d28f.southafricanorth.azurecontainerapps.io'

var tags = {
  Project: 'PVM Integrations'
  Environment: toUpper(environmentName)
  Owner: 'PVM'
  ManagedBy: 'Bicep'
  CostCentre: 'PVM'
  DataClassification: 'Confidential'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module platform 'modules/platform.bicep' = {
  name: 'pvm-${environmentName}-platform'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
    ownerObjectId: ownerObjectId
    postgresAdminPassword: postgresAdminPassword
    apiImageTag: apiImageTag
    workbenchImageTag: workbenchImageTag
    authMode: authMode
    authTenantId: authTenantId
    authApiClientId: authApiClientId
    authWorkbenchClientId: authWorkbenchClientId
    authWorkbenchClientSecret: authWorkbenchClientSecret
    authApiScope: authApiScope
    authNextAuthSecret: authNextAuthSecret
    authBootstrapAdminEmail: authBootstrapAdminEmail
    authBootstrapAdminObjectId: authBootstrapAdminObjectId
    workbenchPublicUrl: workbenchPublicUrl
    tags: tags
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-pvm-integrations-${environmentName}'
  properties: {
    category: 'Cost'
    amount: monthlyBudgetAmountUsd
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-05-01T00:00:00Z'
      endDate: '2036-05-01T00:00:00Z'
    }
    notifications: {
      actual80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: [
          alertEmail
        ]
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          alertEmail
        ]
      }
    }
  }
}

output resourceGroupName string = rg.name
output acrName string = platform.outputs.acrName
output acrLoginServer string = platform.outputs.acrLoginServer
output containerAppsEnvironmentName string = platform.outputs.containerAppsEnvironmentName
output keyVaultName string = platform.outputs.keyVaultName
output postgresServerName string = platform.outputs.postgresServerName
output postgresFullyQualifiedDomainName string = platform.outputs.postgresFullyQualifiedDomainName
output storageAccountName string = platform.outputs.storageAccountName
output serviceBusNamespaceName string = platform.outputs.serviceBusNamespaceName
output userAssignedIdentityId string = platform.outputs.userAssignedIdentityId
output apiUrl string = platform.outputs.apiUrl
output workbenchUrl string = platform.outputs.workbenchUrl

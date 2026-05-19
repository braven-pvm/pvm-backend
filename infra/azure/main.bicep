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

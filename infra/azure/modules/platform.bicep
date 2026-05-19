param location string
param environmentName string
param ownerObjectId string

@secure()
param postgresAdminPassword string

param apiImageTag string = 'qa-latest'
param workbenchImageTag string = 'qa-latest'
param authMode string = 'Entra'
param authTenantId string = ''
param authApiClientId string = ''
param authWorkbenchClientId string = ''

@secure()
param authWorkbenchClientSecret string = ''

param authApiScope string = ''

@secure()
param authNextAuthSecret string = ''

param authBootstrapAdminEmail string = ''
param authBootstrapAdminObjectId string = ''
param workbenchPublicUrl string

param tags object

var suffix = environmentName
var acrName = 'acrpvmintegrations${suffix}'
var acrLocation = 'westeurope'
var apiContainerAppName = 'ca-pvm-api-${suffix}'
var workbenchContainerAppName = 'ca-pvm-workbench-${suffix}'
var logName = 'log-pvm-integrations-${suffix}'
var appInsightsName = 'appi-pvm-integrations-${suffix}'
var containerAppsEnvironmentName = 'cae-pvm-integrations-${suffix}'
var identityName = 'id-pvm-integrations-${suffix}'
var keyVaultName = 'kv-pvm-int-${suffix}'
var storageAccountName = 'stpvmintegrations${suffix}'
var serviceBusNamespaceName = 'sb-pvm-integrations-${suffix}'
var postgresServerName = 'psql-pvm-integrations-${suffix}'
var postgresAdminUser = 'pvmadmin'
var databaseName = 'pvm'
var pvmConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresAdminUser};Password=${postgresAdminPassword};Ssl Mode=Require;Trust Server Certificate=true'

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var keyVaultSecretsOfficerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: acrLocation
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    publicNetworkAccess: 'Enabled'
    softDeleteRetentionInDays: 30
    enablePurgeProtection: true
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource payloadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'payloads'
  properties: {
    publicAccess: 'None'
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusNamespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    minimumTlsVersion: '1.2'
    zoneRedundant: false
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: postgresServerName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    storage: {
      storageSizeGB: 32
      autoGrow: 'Disabled'
    }
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource postgresFirewallAll 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview' = {
  parent: postgres
  name: 'qa-temporary-public-access'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '255.255.255.255'
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource ownerKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, ownerObjectId, 'key-vault-secrets-officer')
  scope: keyVault
  properties: {
    principalId: ownerObjectId
    principalType: 'User'
    roleDefinitionId: keyVaultSecretsOfficerRoleDefinitionId
  }
}

resource identityKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, 'key-vault-secrets-user')
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
  }
}

resource identityStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, 'storage-blob-data-contributor')
  scope: storage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
  }
}

resource identityAcrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, 'acr-pull')
  scope: acr
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'connectionstrings--pvm'
  properties: {
    value: pvmConnectionString
  }
  dependsOn: [
    ownerKeyVaultRole
  ]
}

resource payloadContainerSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'blobstorage--payloadcontainer'
  properties: {
    value: payloadsContainer.name
  }
  dependsOn: [
    ownerKeyVaultRole
  ]
}

resource apiContainerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: apiContainerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'Auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: [
        {
          name: 'connectionstrings-pvm'
          value: pvmConnectionString
        }
      ]
    }
    template: {
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
      containers: [
        {
          name: apiContainerAppName
          image: '${acr.properties.loginServer}/pvm-api:${apiImageTag}'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Pvm'
              secretRef: 'connectionstrings-pvm'
            }
            {
              name: 'Auth__Mode'
              value: authMode
            }
            {
              name: 'Auth__TenantId'
              value: authTenantId
            }
            {
              name: 'Auth__Audience'
              value: authApiClientId
            }
            {
              name: 'Auth__BootstrapAdminEmails__0'
              value: authBootstrapAdminEmail
            }
            {
              name: 'Auth__BootstrapAdminObjectIds__0'
              value: authBootstrapAdminObjectId
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    identityAcrPullRole
  ]
}

resource workbenchContainerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: workbenchContainerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 3000
        transport: 'Auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: [
        {
          name: 'auth-entra-client-secret'
          value: authWorkbenchClientSecret
        }
        {
          name: 'auth-nextauth-secret'
          value: authNextAuthSecret
        }
      ]
    }
    template: {
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
      containers: [
        {
          name: workbenchContainerAppName
          image: '${acr.properties.loginServer}/pvm-workbench:${workbenchImageTag}'
          env: [
            {
              name: 'NODE_ENV'
              value: 'production'
            }
            {
              name: 'AUTH_MODE'
              value: authMode
            }
            {
              name: 'AUTH_ENTRA_TENANT_ID'
              value: authTenantId
            }
            {
              name: 'AUTH_ENTRA_CLIENT_ID'
              value: authWorkbenchClientId
            }
            {
              name: 'AUTH_ENTRA_CLIENT_SECRET'
              secretRef: 'auth-entra-client-secret'
            }
            {
              name: 'AUTH_API_SCOPE'
              value: authApiScope
            }
            {
              name: 'AUTH_DEBUG'
              value: 'true'
            }
            {
              name: 'NEXTAUTH_URL'
              value: workbenchPublicUrl
            }
            {
              name: 'NEXTAUTH_SECRET'
              secretRef: 'auth-nextauth-secret'
            }
            {
              name: 'NEXT_PUBLIC_API_BASE_URL'
              value: 'https://${apiContainerApp.properties.configuration.ingress.fqdn}'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    identityAcrPullRole
  ]
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output containerAppsEnvironmentName string = containerAppsEnvironment.name
output containerAppsEnvironmentId string = containerAppsEnvironment.id
output keyVaultName string = keyVault.name
output postgresServerName string = postgres.name
output postgresFullyQualifiedDomainName string = postgres.properties.fullyQualifiedDomainName
output storageAccountName string = storage.name
output serviceBusNamespaceName string = serviceBus.name
output userAssignedIdentityId string = identity.id
output userAssignedIdentityClientId string = identity.properties.clientId
output apiUrl string = 'https://${apiContainerApp.properties.configuration.ingress.fqdn}'
output workbenchUrl string = 'https://${workbenchContainerApp.properties.configuration.ingress.fqdn}'

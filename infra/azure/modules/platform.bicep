param location string
param environmentName string
param ownerObjectId string
param alertEmail string

@secure()
param postgresAdminPassword string

param apiImageTag string = 'qa-latest'
param workbenchImageTag string = 'qa-latest'
param workerImageTag string = 'qa-latest'
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

param shopriteBaseUrl string = ''

@secure()
param shopriteUsername string = ''

@secure()
param shopritePassword string = ''

param shopriteInvoiceSubmissionMode string = 'LocalStub'
param acumaticaInvoiceSourceMode string = 'Fixture'
param acumaticaBaseUrl string = ''
param acumaticaCompany string = ''
param acumaticaBranch string = ''

@secure()
param acumaticaUsername string = ''

@secure()
param acumaticaPassword string = ''

param acumaticaEndpointName string = 'Default'
param acumaticaEndpointVersion string = '24.200.001'
param acumaticaCustomerAccounts array = []
param acumaticaParentCustomerAccounts array = []
param acumaticaInvoiceDateFrom string = ''
param acumaticaPageSize int = 100
param containerAppMinReplicas int = 1

param tags object

var suffix = environmentName
var acrName = 'acrpvmintegrations${suffix}'
var acrLocation = 'westeurope'
var apiContainerAppName = 'ca-pvm-api-${suffix}'
var workbenchContainerAppName = 'ca-pvm-workbench-${suffix}'
var workerContainerAppName = 'ca-pvm-worker-${suffix}'
var purchaseOrderRefreshJobName = 'job-pvm-po-refresh-${suffix}'
var logName = 'log-pvm-integrations-${suffix}'
var appInsightsName = 'appi-pvm-integrations-${suffix}'
var containerAppsEnvironmentName = 'cae-pvm-integrations-${suffix}'
var identityName = 'id-pvm-integrations-${suffix}'
var keyVaultName = 'kv-pvm-intg-${suffix}'
var storageAccountName = 'stpvmintegrations${suffix}'
var serviceBusNamespaceName = 'sb-pvm-integrations-${suffix}'
var shopritePurchaseOrderRefreshQueueName = 'shoprite-po-refresh'
var acumaticaInvoiceDiscoveryQueueName = 'acumatica-invoice-discovery'
var shopriteInvoiceSubmitQueueName = 'shoprite-invoice-submit'
var postgresServerName = 'psql-pvm-integrations-${suffix}'
var postgresAdminUser = 'pvmadmin'
var databaseName = 'pvm'
var pvmConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresAdminUser};Password=${postgresAdminPassword};Ssl Mode=Require;Trust Server Certificate=true'
var hasAcumaticaCredentials = !empty(acumaticaUsername) && !empty(acumaticaPassword)
var acumaticaCredentialSecrets = hasAcumaticaCredentials ? [
  {
    name: 'acumatica-username'
    value: acumaticaUsername
  }
  {
    name: 'acumatica-password'
    value: acumaticaPassword
  }
] : []
var apiSecrets = concat([
  {
    name: 'connectionstrings-pvm'
    value: pvmConnectionString
  }
  {
    name: 'shoprite-username'
    value: shopriteUsername
  }
  {
    name: 'shoprite-password'
    value: shopritePassword
  }
], acumaticaCredentialSecrets)
var acumaticaCredentialEnvironment = hasAcumaticaCredentials ? [
  {
    name: 'Acumatica__Username'
    secretRef: 'acumatica-username'
  }
  {
    name: 'Acumatica__Password'
    secretRef: 'acumatica-password'
  }
] : []
var acumaticaCustomerEnvironment = map(acumaticaCustomerAccounts, (account, index) => {
  name: 'Acumatica__CustomerAccounts__${index}'
  value: account
})
var acumaticaParentCustomerEnvironment = map(acumaticaParentCustomerAccounts, (account, index) => {
  name: 'Acumatica__ParentCustomerAccounts__${index}'
  value: account
})
var apiEnvironment = concat([
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'Pvm__EnvironmentName'
    value: toUpper(environmentName)
  }
  {
    name: 'ShopritePoRefresh__ScheduleIntervalMinutes'
    value: '5'
  }
  {
    name: 'ShopritePoRefresh__StaleAfterMinutes'
    value: '15'
  }
  {
    name: 'Automation__Mode'
    value: 'Disabled'
  }
  {
    name: 'ConnectionStrings__Pvm'
    secretRef: 'connectionstrings-pvm'
  }
  {
    name: 'PayloadArchive__Provider'
    value: 'AzureBlob'
  }
  {
    name: 'PayloadArchive__ContainerName'
    value: 'payloads'
  }
  {
    name: 'PayloadArchive__ServiceUri'
    value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}'
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: identity.properties.clientId
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
  {
    name: 'Shoprite__BaseUrl'
    value: shopriteBaseUrl
  }
  {
    name: 'Shoprite__Username'
    secretRef: 'shoprite-username'
  }
  {
    name: 'Shoprite__Password'
    secretRef: 'shoprite-password'
  }
  {
    name: 'Shoprite__InvoiceSubmissionMode'
    value: shopriteInvoiceSubmissionMode
  }
  {
    name: 'Acumatica__InvoiceSourceMode'
    value: acumaticaInvoiceSourceMode
  }
  {
    name: 'Acumatica__BaseUrl'
    value: acumaticaBaseUrl
  }
  {
    name: 'Acumatica__Company'
    value: acumaticaCompany
  }
  {
    name: 'Acumatica__Branch'
    value: acumaticaBranch
  }
  {
    name: 'Acumatica__EndpointName'
    value: acumaticaEndpointName
  }
  {
    name: 'Acumatica__EndpointVersion'
    value: acumaticaEndpointVersion
  }
  {
    name: 'Acumatica__CountryCode'
    value: 'ZA'
  }
  {
    name: 'Acumatica__PageSize'
    value: string(acumaticaPageSize)
  }
  {
    name: 'Acumatica__InvoiceDateFrom'
    value: acumaticaInvoiceDateFrom
  }
], acumaticaCredentialEnvironment, acumaticaCustomerEnvironment, acumaticaParentCustomerEnvironment)
var workerEnvironment = concat(apiEnvironment, [
  {
    name: 'ServiceBus__FullyQualifiedNamespace'
    value: '${serviceBus.name}.servicebus.windows.net'
  }
  {
    name: 'ServiceBus__MaxDeliveryCount'
    value: '5'
  }
])

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var keyVaultSecretsOfficerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var serviceBusDataSenderRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var serviceBusDataReceiverRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

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
    isVersioningEnabled: true
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

resource serviceBusQueues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for queueName in [
  shopritePurchaseOrderRefreshQueueName
  acumaticaInvoiceDiscoveryQueueName
  shopriteInvoiceSubmitQueueName
]: {
  parent: serviceBus
  name: queueName
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    duplicateDetectionHistoryTimeWindow: 'P1D'
    enableBatchedOperations: true
    enablePartitioning: false
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    requiresDuplicateDetection: true
  }
}]

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

resource identityServiceBusSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, identity.id, 'service-bus-data-sender')
  scope: serviceBus
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: serviceBusDataSenderRoleDefinitionId
  }
}

resource identityServiceBusReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, identity.id, 'service-bus-data-receiver')
  scope: serviceBus
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: serviceBusDataReceiverRoleDefinitionId
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
      secrets: apiSecrets
    }
    template: {
      scale: {
        minReplicas: containerAppMinReplicas
        maxReplicas: 2
      }
      containers: [
        {
          name: apiContainerAppName
          image: '${acr.properties.loginServer}/pvm-api:${apiImageTag}'
          env: apiEnvironment
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
        minReplicas: containerAppMinReplicas
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
              value: 'false'
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
            {
              name: 'NEXT_PUBLIC_PVM_ENVIRONMENT_NAME'
              value: toUpper(environmentName)
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

resource workerContainerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: workerContainerAppName
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
      registries: [
        {
          server: acr.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: apiSecrets
    }
    template: {
      scale: {
        // Keep one worker alive so the database outbox drains even when all broker queues are empty.
        minReplicas: 1
        maxReplicas: 3
        rules: [for queueName in [
          shopritePurchaseOrderRefreshQueueName
          acumaticaInvoiceDiscoveryQueueName
          shopriteInvoiceSubmitQueueName
        ]: {
          name: replace(queueName, '-', '')
          custom: {
            type: 'azure-servicebus'
            metadata: {
              namespace: serviceBus.name
              queueName: queueName
              messageCount: '5'
            }
            auth: []
            identity: identity.id
          }
        }]
      }
      containers: [
        {
          name: workerContainerAppName
          image: '${acr.properties.loginServer}/pvm-worker:${workerImageTag}'
          env: workerEnvironment
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
    identityServiceBusSenderRole
    identityServiceBusReceiverRole
    serviceBusQueues
  ]
}

resource purchaseOrderRefreshJob 'Microsoft.App/jobs@2025-01-01' = {
  name: purchaseOrderRefreshJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 300
      replicaRetryLimit: 1
      scheduleTriggerConfig: {
        cronExpression: '*/5 * * * *'
        parallelism: 1
        replicaCompletionCount: 1
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
      containers: [
        {
          name: purchaseOrderRefreshJobName
          image: '${acr.properties.loginServer}/pvm-worker:${workerImageTag}'
          args: [
            '--enqueue-shoprite-po-refresh'
          ]
          env: [
            {
              name: 'ConnectionStrings__Pvm'
              secretRef: 'connectionstrings-pvm'
            }
            {
              name: 'Pvm__EnvironmentName'
              value: toUpper(environmentName)
            }
            {
              name: 'ShopritePoRefresh__ScheduleIntervalMinutes'
              value: '5'
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

resource operationsActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-pvm-integrations-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'PVM Intg'
    enabled: true
    emailReceivers: [
      {
        name: 'PVM integration operator'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource stalePurchaseOrderRefreshAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'alert-pvm-po-refresh-stale-${suffix}'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: 'PVM Shoprite PO refresh is stale (${toUpper(environmentName)})'
    description: 'No successful Shoprite purchase-order refresh completed in the last 15 minutes.'
    enabled: true
    severity: 2
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      logAnalytics.id
    ]
    autoMitigate: true
    skipQueryValidation: false
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == "${workerContainerAppName}"
            | where Log_s contains "integration.run.completed"
            | where Log_s contains "RunType=shoprite-po-refresh"
          '''
          timeAggregation: 'Count'
          operator: 'LessThan'
          threshold: 1
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        operationsActionGroup.id
      ]
    }
  }
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
output workerContainerAppName string = workerContainerApp.name
output purchaseOrderRefreshJobName string = purchaseOrderRefreshJob.name

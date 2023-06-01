# Login to Azure
az login

# Select the appropriate subscription (if necessary)
az account set --subscription 'your-subscription-id'

# Get the Vault Name
$vaultName = 'your-vault-name'

# Get the secrets from the vault
$secrets = az keyvault secret list --vault-name $vaultName | ConvertFrom-Json

# Initialize an empty array to hold the secrets
$secretArray = @()

# Iterate through each secret
foreach ($secret in $secrets) {
    # Get the secret value
    $secretValue = az keyvault secret show --name $secret.name --vault-name $vaultName | ConvertFrom-Json
    # Add the secret to the array
    $secretArray += @{
        'id'    = $secret.id
        'name'  = $secret.name
        'value' = $secretValue.value
    }
}

# Export the secrets to a JSON file
$secretArray | ConvertTo-Json | Out-File -FilePath .\secrets.json

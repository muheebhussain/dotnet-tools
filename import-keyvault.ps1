# Login to Azure
az login

# Select the appropriate subscription (if necessary)
az account set --subscription 'your-second-subscription-id'

# Get the Vault Name
$vaultName = 'your-second-vault-name'

# Read the secrets from the JSON file
$secrets = Get-Content -Path .\secrets.json | ConvertFrom-Json

# Iterate through each secret
foreach ($secret in $secrets) {
    # Create or update the secret in the vault
    az keyvault secret set --name $secret.name --value $secret.value --vault-name $vaultName
}

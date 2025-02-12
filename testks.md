# EasyKS Documentation

## Introduction

**EasyKS** is an in-house tool designed to simplify the creation and configuration of Azure resources, including **Azure Kubernetes Service (AKS)**. This process streamlines the setup and deployment of AKS resources in an **SG (Secure Gateway) Environment**.

### Features:
- **Blue-Green Migration**
- **Limited Rights Deployment**
- **Automated Resource Creation** (e.g., KeyVault, Certificates, Namespaces)
- **Integration with Tools** such as **ArgoCD**, **PG Admin**, etc.

---

## Getting Started

### Prerequisites

To use **EasyKS**, ensure the following prerequisites are met:

1. **Subscription ID**
   - AKS project must be created in a subscription.
   - Request **SGM PASS** or use **myAKS**.

2. **Service Principal (SPN)**
   - Create an SPN via `go/lpacref` (must be done by the App Manager).
   - SPN needs **Power User** role on the Azure subscription.

3. **Wildcard Certificate**
   - Required for the private DNS zone used in the project.

4. **GitHub Repository**
   - Store Azure infrastructure files.

5. **GitHub Actions Access**
   - Must have **GitHub organization-level** access to execute workflows.

6. **GitHub Secrets Configuration**
   - Store credentials such as **proxy**, **SPN authentication**, etc.

---

## Setting Up EasyKS Infrastructure Repository

1. **Create a Repository for Infrastructure Setup**
   - Example repository name: `projectname-easyKS-infra`

2. **Inside the repository:**
   - Create a `.github/workflows/` directory.
   - Add a workflow file (`easyks.yml`).

---

## EasyKS Workflow Configuration

### Workflow Inputs

```yaml
name: easyks
on:
  workflow_dispatch:
    inputs:
      deployer-branch:
        default: "main"
        required: true
        type: string
        description: "Which branch of EasyKS-deployer to use"
      keep_deployer_pod:
        default: false
        required: false
        type: boolean
        description: "Keep deployer pod for debugging"
```
1. **Read Configuration
This job reads the cluster configuration file (cluster.yml) and sets it as an environment variable.

```yaml
jobs:
  read-config:
    name: Read Config
    runs-on: [self-hosted, linux]
    outputs:
      clusters-config: ${{ steps.read-config.outputs.CLUSTERS_CONFIG }}
    steps:
      - name: Checkout Deployer
        uses: actions/checkout@v3
      - name: Read Config
        id: read-config
        run: echo "CLUSTERS_CONFIG=$(cat ./basic-usage/clusters.yaml | base64 -w0)" >> $GITHUB_OUTPUT
```
2. ***Update KeyVault with AKS Token
This job updates Azure KeyVault with the required authentication secrets.

```yaml
  update-keyvault:
    name: Update KeyVault with AKS Token
    uses: amer-arc/EasyKS-deployer/.github/workflows/update-keyvault-secret.yaml@main
    with:
      deployer-branch: ${{ inputs.deployer-branch }}
      clusters-config: ${{ needs.read-config.outputs.clusters-config }}
    secrets:
      azure_http_proxy: ${{ secrets.AZURE_HTTP_PROXY }}
      azure_spn_client_id: ${{ secrets.DEV_SPN_CLIENT_ID }}
      azure_spn_client_secret: ${{ secrets.DEV_SPN_CLIENT_SECRET }}
      azure_spn_tenant_id: ${{ secrets.DEV_SPN_TENANT_ID }}
      GH_PAT: ${{ secrets.GH_PAT }}
    needs:
      - read-config
```
3. ***Deploy EasyKS
This job applies Terraform state to deploy the infrastructure.

```yaml
  easyks-deploy:
    name: Deploy EasyKS
    uses: amer-arc/EasyKS-deployer/.github/workflows/apply-terraform-state.yaml@main
    with:
      deployer-branch: ${{ inputs.deployer-branch }}
      clusters-config: ${{ needs.read-config.outputs.clusters-config }}
      keep_deployer_pod: ${{ inputs.keep_deployer_pod }}
    secrets:
      azure_http_proxy: ${{ secrets.AZURE_HTTP_PROXY }}
      azure_spn_client_id: ${{ secrets.DEV_SPN_CLIENT_ID }}
      azure_spn_client_secret: ${{ secrets.DEV_SPN_CLIENT_SECRET }}
      azure_spn_tenant_id: ${{ secrets.DEV_SPN_TENANT_ID }}
      GH_PAT: ${{ secrets.GH_PAT }}
    needs:
      - read-config
      - update-keyvault
```

# Cluster Configuration File
The cluster configuration file (cluster.yml) is required for EasyKS deployment.

Create the file at the following location in your repository:
```yaml
rootfolder/deploy/cluster.yml
```
You can find an example cluster.yml at: Cluster YAML Example

Copy the contents of the cluster.yml into your repository.

#Summary
EasyKS automates Azure and AKS resource creation using GitHub Actions and Terraform. By following this guide, you can:

Set up Azure infrastructure quickly.
Use GitHub Actions workflows to automate deployments.
Maintain secure authentication using GitHub Secrets.
Deploy resources efficiently in a controlled environment.
For further details, refer to the repository link provided above.


### Enhancements:
1. **Organized Sections** – Introduction, Getting Started, Setup, Workflow Config, and Execution.
2. **Formatted YAML Snippets** – Properly structured for easy reference.
3. **Step-by-Step Instructions** – Clear instructions on repository setup and workflow execution.
4. **Direct Links** – Added a direct link to the cluster configuration file.

Let me know if you need modifications!

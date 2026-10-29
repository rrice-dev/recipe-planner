# Encrypting the Data Protection keys with Key Vault

The API's Data Protection keys sign login cookies and encrypt Kroger tokens and reset links. They're stored in the database so every container shares them, but without this setup they're stored **in plain text**, next to the data they protect. Anyone who got a copy of the database could forge a login for any account and decrypt Kroger tokens.

With this setup, each key is encrypted with a Key Vault key before it's stored, and only the API's managed identity can unwrap it.

**Cost:** a few cents a month at most. Key Vault charges per operation, and the API only unwraps keys when a container starts.

**What happens on the first start after setup:** the API deletes the old plain-text key. Everyone is signed out once, and anyone with a Kroger connection reconnects it once.

## Steps

Run these in a terminal where you're signed in with `az login`. Pick a vault name; it must be unique across all of Azure (3 to 24 letters, digits and hyphens).

```bash
RG=recipe-planner-rg
KV=recipe-kv-<something-unique>
LOC=$(az group show --name $RG --query location -o tsv)
```

**1. Create the vault.** It uses Azure role-based access, so permissions are role assignments.

```bash
az keyvault create --name $KV --resource-group $RG --location $LOC --enable-rbac-authorization true --output none
KV_ID=$(az keyvault show --name $KV --query id -o tsv)
```

**2. Give yourself permission to create keys in it.**

```bash
ME=$(az ad signed-in-user show --query id -o tsv)
az role assignment create --assignee-object-id $ME --assignee-principal-type User \
  --role "Key Vault Crypto Officer" --scope $KV_ID --output none
```

Role assignments can take a minute or two to apply. If the next step says you're not authorized, wait and try again.

**3. Create the key.**

```bash
az keyvault key create --vault-name $KV --name dataprotection --kty RSA --size 2048 --output none
KEY_ID=$(az keyvault key show --vault-name $KV --name dataprotection --query key.kid -o tsv)
echo $KEY_ID
```

It prints an address like `https://<vault>.vault.azure.net/keys/dataprotection/<version>`.

**4. Let the API use the key.** The API already has a system-assigned managed identity (it's how it reaches photo storage).

```bash
API_ID=$(az containerapp identity show --name recipe-api --resource-group $RG --query principalId -o tsv)
az role assignment create --assignee-object-id $API_ID --assignee-principal-type ServicePrincipal \
  --role "Key Vault Crypto User" --scope $KV_ID --output none
```

**5. Turn it on.** Wait a minute or two after step 4, so the role is in place before the API starts.

```bash
az containerapp update --name recipe-api --resource-group $RG \
  --set-env-vars DataProtection__KeyVaultKeyId=$KEY_ID --output none
```

**6. Check it worked.** Open the site, sign in, and confirm you stay signed in. In the API's logs (Application Insights, or **Log stream** on the Container App), look for:

- `Deleted 1 unencrypted Data Protection key(s)` on the first start. That's expected.
- The warning `Data Protection keys are stored unencrypted` should no longer appear.

If sign-in fails with errors about Key Vault, the role from step 4 probably hasn't applied yet. Wait a few minutes, then restart the API from the Container App's **Revisions and replicas** page.

## Rotating the key later (optional)

Run `az keyvault key rotate --vault-name $KV --name dataprotection`, then repeat step 5 with the new `KEY_ID`. Older keys stay readable because Key Vault keeps every version of the key.

## Database access

The database server was created with `--public-access 0.0.0.0`, which lets any service running in Azure (anyone's, not just yours) try to connect. Its password is the only protection. Two ways to tighten that, easiest first:

1. **Make sure the password is long and random** (20+ characters from a password manager), and that the connection string keeps `SSL Mode=Require`. This is the minimum.
2. **Only allow the API's addresses.** Find them with `az containerapp show --name recipe-api --resource-group $RG --query properties.outboundIpAddresses`. Add a firewall rule for each, then remove the rule that allows all Azure services. The catch: on the Consumption plan, Azure can change these addresses. If that happens, the API loses its database connection until you update the rules. Weigh that before doing it.

The complete fix is private networking (a virtual network for the Container Apps environment, and private access for the database). A database's network mode can't be changed after it's created, so that means a new server and moving the data across. It's worth it if this app ever holds data that matters more than recipes.

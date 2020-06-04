# Video indexer application using Dapr

## Running locally in Standalone mode (Windows file system) 

Refer to [Dapr's documentation](https://github.com/dapr/docs/blob/master/getting-started/environment-setup.md) for more details.

- Open PowerShell as Administrator and run: 

```powershell
    powershell -Command "iwr -useb https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1 | iex"
```

- Open Command Prompt as Administrator and run:

```cmd
    dapr init
```

- Check Dapr runtime containers:

```cmd
    docker ps

    CONTAINER ID        IMAGE               COMMAND                  CREATED             STATUS              PORTS                     NAMES
    523eed39c32d        daprio/dapr         "./placement"            8 hours ago         Up 29 minutes       0.0.0.0:6050->50005/tcp   dapr_placement
    7f9c30b58968        redis               "docker-entrypoint.s…"   8 hours ago         Up 29 minutes       0.0.0.0:6379->6379/tcp    dapr_redis
```

- [Get Ngrok](https://ngrok.com/download)

- Run Ngrok with following command

```
    ngrok http -host-header=localhost 9000
```

- Configure your event grid binding (eg. `/components/eventgrid.yaml`) with [required metadata](https://github.com/dapr/docs/blob/master/reference/specs/bindings/eventgrid.md), using the HTTPS endpoint from _ngrok_ for `subscriberEndpoint`

```yaml
..
spec
  - name: subscriberEndpoint
    value: "https://xxx.ngrok.io/api/events"    
  - name: handshakePort
    value: 9000
  ..
```

- Configure your cosmosdb binding (eg. `/components/cosmosdb.yaml`) with [required metadata](https://github.com/dapr/docs/blob/master/reference/specs/bindings/cosmosdb.md)

- [Start application with Dapr](https://github.com/dapr/docs/blob/master/walkthroughs/darprun.md)

```
    dapr run --app-id video-indexer --app-port 5000 --port 3500 dotnet run
```

## Steps to deploy to Kubernetes

1) Create Kubernetes cluster and get admin credentials

2) Install Helm 3 from script: https://helm.sh/docs/intro/install/

```bash
curl https://raw.githubusercontent.com/helm/helm/master/scripts/get-helm-3 | sudo bash
```

3) Install Dapr using Helm3: https://github.com/dapr/docs/blob/master/getting-started/environment-setup.md#using-helm-advanced

```bash
helm repo add dapr https://daprio.azurecr.io/helm/v1/repo
helm repo update

kubectl create namespace dapr-system
helm install dapr dapr/dapr --namespace dapr-system
kubectl get pods -n dapr-system -w
```

4) Install Redis

```bash
helm repo add bitnami https://charts.bitnami.com/bitnami
helm install redis bitnami/redis --set "usePassword=false"
```

5) Install Nginx with Dapr annotations to default namespace

```bash
helm repo add stable https://kubernetes-charts.storage.googleapis.com/
helm install nginx stable/nginx-ingress -f ./deploy/dapr-annotations.yaml -n default

# Get public IP from ingress controller
kubectl get svc -l component=controller -o jsonpath='Public IP is: {.items[0].status.loadBalancer.ingress[0].ip}{"\n"}'
```

6) Configure DNS A record pointing to the public IP

7) Install Cert manager to default namespace

```bash
# Install the CustomResourceDefinition resources separately
kubectl apply --validate=false -f https://raw.githubusercontent.com/jetstack/cert-manager/release-0.13/deploy/manifests/00-crds.yaml

# Label the ingress-basic namespace to disable resource validation
kubectl label namespace default cert-manager.io/disable-validation=true

# Add the Jetstack Helm repository
helm repo add jetstack https://charts.jetstack.io

# Update your local Helm chart repository cache
helm repo update

# Install the cert-manager Helm chart
helm install \
  cert-manager \
  --namespace default \
  --version v0.13.0 \
  jetstack/cert-manager
```

8) Create a CA cluster issuer: https://docs.microsoft.com/en-us/azure/aks/ingress-tls#create-a-ca-cluster-issuer 

9) Add Azure Key cert file to k8s secret for enabling secrets in Dapr bindings: https://github.com/dapr/docs/blob/master/howto/setup-secret-store/azure-keyvault.md#use-azure-key-vault-secret-store-in-kubernetes-mode

```bash
kubectl create secret generic azure-keyvault-cert --from-file=VideoIndexerCert.pfx
```

10) Deploy akv2k8s for k8s app's secrets: https://akv2k8s.io/installation/installing-with-helm/#installing-with-helm-on-azure-aks

```bash
kubectl create ns akv2k8s
helm repo add spv-charts http://charts.spvapi.no
helm repo update

helm install azure-key-vault-controller \
  spv-charts/azure-key-vault-controller \
  --namespace akv2k8s

helm install azure-key-vault-env-injector \
  spv-charts/azure-key-vault-env-injector \
  --set installCrd=false \
  --namespace akv2k8s

kubectl apply -f ./deploy/azurekeyvaultsecrets.yaml

$ kubectl get akvs

NAME                           VAULT                 VAULT OBJECT              SECRET NAME   SYNCHED
secret-client-id               video-indexer-we-kv   clientId
secret-client-secret           video-indexer-we-kv   clientSecret
secret-storage-account-name    video-indexer-we-kv   azureStorageAccountName
secret-tenant-id               video-indexer-we-kv   tenantId
secret-video-indexer-api-key   video-indexer-we-kv   videoIndexerApiKey

# Remember to authorize get permission for k8s cluster's spn: https://akv2k8s.io/tutorials/prerequisites/#add-secret---required-for-secret-tutorials
# Otherwise akv2k8s fails to initialize along with your app's pod

```

10) Deploy Dapr components

```bash
kubectl apply -f ./deploy/azurekeyvault.yaml
kubectl apply -f ./deploy/redis.yaml
kubectl apply -f ./deploy/eventgrid_binding.yaml
kubectl apply -f ./deploy/cosmosdb_binding.yaml
```

11) Deploy app and ingress

```bash
kubectl apply -f ./deploy/dotnetwebapi.yaml
```

12) Check Nginx controller logs and restart pod if needed (https://kubernetes.github.io/ingress-nginx/troubleshooting/)

```bash
$ kubectl get pods -l app=nginx-ingress

NAME                                                   READY   STATUS    RESTARTS   AGE
nginx-nginx-ingress-controller-649df94867-fp6mg        2/2     Running   0          51m
nginx-nginx-ingress-default-backend-6d96c457f6-4nbj5   1/1     Running   0          55m

$ kubectl logs nginx-nginx-ingress-controller-649df94867-fp6mg nginx-ingress-controller

# If you see 503s logged from calls to webhook endpoint '/api/events' restart the pod
# .."OPTIONS /api/events HTTP/1.1" 503..

kubectl delete pod nginx-nginx-ingress-controller-649df94867-fp6mg
```

12) Observe your Event Subscription being created to Storage account

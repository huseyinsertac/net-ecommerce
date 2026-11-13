# Server setup runbook (one-time)

This is the manual, one-time setup that lets the `Deploy` workflow publish VoltLane to a single VPS.
After it is done, every merge to `main` deploys automatically: CI passes, images go to GHCR, the
server pulls them, and a health check confirms the site is up.

Assumptions: a fresh Ubuntu 24.04 server (for example Hetzner Cloud CX22) and a GitHub repository
with this code on `main`. Replace `<SERVER_IP>`, `<YOUR_DOMAIN>` and `<owner-in-lowercase>` in prose and in the variables at the top of each command block.

## 1. Create the GitHub repository

Create the repository, then push `main`. Note the owner (user or organisation) name; images are
published to `ghcr.io/<owner-in-lowercase>/voltlane-{api,web}`.

## 2. Create the server

Create an Ubuntu 24.04 server (Hetzner Cloud CX22 is plenty) and add your own SSH public key
during creation. Note its public IPv4 address (`<SERVER_IP>`).

## 3. DNS

Point an `A` record for your domain at `<SERVER_IP>` and wait for it to resolve
(`dig +short <YOUR_DOMAIN>`).

No domain? Use a free `sslip.io` name that encodes the IP, for example `203-0-113-10.sslip.io` for
`203.0.113.10`. Let's Encrypt issues certificates for it. Use that name as `<YOUR_DOMAIN>` below.

## 4. Prepare the server

SSH in as root (or your admin user): `ssh root@<SERVER_IP>`.

### 4.1 Install Docker Engine and the Compose plugin (official apt repo)

```bash
apt-get update
apt-get install -y ca-certificates curl
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc

tee /etc/apt/sources.list.d/docker.sources <<SRC
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Signed-By: /etc/apt/keyrings/docker.asc
SRC

apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker
docker compose version
```

### 4.2 Create the `deploy` user

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /opt/voltlane
chown deploy:deploy /opt/voltlane
```

### 4.3 Firewall

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp   # HTTP/3 (QUIC)
ufw --force enable
ufw status
```

### 4.4 Create the production `.env`

Still as root, set your two values, then paste the whole block. It writes the file as root and
hands it to `deploy`, so the deploy user can read it:

```bash
DOMAIN=your-domain.example      # bare domain, no https://
OWNER=your-github-owner         # lowercase

install -m 600 -o deploy -g deploy /dev/null /opt/voltlane/.env
cat > /opt/voltlane/.env <<ENV
POSTGRES_USER=voltlane
POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')
SITE_ADDRESS=${DOMAIN}
GHCR_OWNER=${OWNER}
IMAGE_TAG=latest
ENV
chown deploy:deploy /opt/voltlane/.env
chmod 600 /opt/voltlane/.env
ls -l /opt/voltlane/.env       # expect: -rw------- 1 deploy deploy
```

`SITE_ADDRESS` must be the bare domain (no `https://`); Caddy then obtains a Let's Encrypt
certificate automatically. The deploy workflow overrides `GHCR_OWNER` and `IMAGE_TAG` on every
run, so those two values only matter for manual `docker compose` use.

## 5. Create the deploy SSH key

On your own machine (not the server):

```bash
ssh-keygen -t ed25519 -C "voltlane-deploy" -f ./voltlane_deploy -N ""
```

Install the public key for the `deploy` user (run on the server as root):

Replace the placeholder on the first line with the single line from `voltlane_deploy.pub`, then
paste the block:

```bash
PUBKEY="ssh-ed25519 AAAA... voltlane-deploy"
install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
echo "$PUBKEY" >> /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys
chmod 600 /home/deploy/.ssh/authorized_keys
```

Check from your machine: `ssh -i ./voltlane_deploy deploy@<SERVER_IP> docker ps` should work.

In GitHub, under **Settings > Secrets and variables > Actions**, add:

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `DEPLOY_HOST` | `<SERVER_IP>` |
| Secret | `DEPLOY_USER` | `deploy` |
| Secret | `DEPLOY_SSH_KEY` | full contents of the private key file `voltlane_deploy` |
| Secret | `DEPLOY_HOST_FINGERPRINT` | server host key fingerprint, `SHA256:...` (command below the table) |
| Variable | `SITE_URL` | `https://<YOUR_DOMAIN>` (no trailing slash) |

Pin the server's SSH host key so the workflow cannot be tricked into deploying to an impostor.
On the server, print its fingerprint and copy the whole `SHA256:...` value:

```bash
ssh-keygen -l -E sha256 -f /etc/ssh/ssh_host_ed25519_key.pub
```

Add it as secret `DEPLOY_HOST_FINGERPRINT` (listed in the table above).

Then delete the local private key file once it is stored in GitHub.

## 6. First deploy and GHCR package visibility

Merge to `main` (or run the **Deploy** workflow manually from the Actions tab). The first run
pushes the images; the server's `pull` may fail with `denied` because new GHCR packages are
private by default. Fix it once:

1. GitHub profile (or organisation) > **Packages** > `voltlane-api` > **Package settings**.
2. Under **Danger Zone**, **Change visibility** > **Public**. Repeat for `voltlane-web`.
3. Re-run the failed Deploy workflow.

(Alternative for private packages: run `docker login ghcr.io` as `deploy` on the server with a
read-only `read:packages` personal access token.)

## 7. Verify

- The Deploy workflow is green, including the final health-check step.
- `https://<YOUR_DOMAIN>` loads with a valid certificate and the footer shows `API <commit sha>`.
- `https://<YOUR_DOMAIN>/health/ready` returns `Healthy`; `https://<YOUR_DOMAIN>/scalar` loads.
- On the server: `cd /opt/voltlane && docker compose -f docker-compose.prod.yml ps` shows all four
  services running.

## Troubleshooting

- **Certificate not issued / browser TLS error.** Check DNS (`dig +short <YOUR_DOMAIN>` must return
  the server IP), that ports 80 and 443 are open in `ufw` and in any cloud firewall, and read
  Caddy's log: `docker compose -f docker-compose.prod.yml logs caddy`. Let's Encrypt rate-limits
  repeated failures, so fix DNS and ports before retrying.
- **`denied` or `unauthorized` when pulling images.** The GHCR packages are still private. Make
  both public (step 6) or log in on the server. Also check `GHCR_OWNER` is lowercase.
- **SSH step fails (`handshake failed`).** The `DEPLOY_SSH_KEY` secret must be the complete private
  key including the `BEGIN`/`END` lines, and the matching public key must be in
  `/home/deploy/.ssh/authorized_keys`.
- **Health check times out.** On the server run
  `docker compose -f docker-compose.prod.yml logs api --tail 100`, and confirm `SITE_URL` is the
  full `https://` URL.

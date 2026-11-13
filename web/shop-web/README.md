# VoltLane web

Angular storefront for [VoltLane](../README.md): standalone components, Angular Material, signals.
In production it is built into a static image served by Caddy (see `Dockerfile` and `Caddyfile.web`).

## Commands

```bash
npm ci               # install
npx ng serve         # dev server on http://localhost:4200
npx ng lint          # lint
npx ng test --no-watch
npx ng build         # production build into dist/
```

`ng serve` proxies `/api` and `/health` to the API on http://localhost:5080 (see `proxy.conf.json`),
so start the API first. The full local setup is described in the
[root README](../README.md#develop-from-source).

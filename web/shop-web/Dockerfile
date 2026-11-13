FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build

FROM caddy:2-alpine
COPY Caddyfile.web /etc/caddy/Caddyfile
COPY --from=build /app/dist/shop-web/browser /srv
EXPOSE 80

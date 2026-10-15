# Production image: build the SPA, serve it with a minimal static server.
# The edge Caddy (TLS, routing) reverse-proxies to this container.
FROM node:26-alpine@sha256:e88a35be04478413b7c71c455cd9865de9b9360e1f43456be5951032d7ac1a66 AS build

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Runs type-check + vite build (see package.json).
RUN npm run build

FROM caddy:2-alpine@sha256:5f5c8640aae01df9654968d946d8f1a56c497f1dd5c5cda4cf95ab7c14d58648

RUN adduser -D -u 1000 app
USER app
# Caddy writes its state under XDG dirs; keep them off the read-only root.
ENV XDG_DATA_HOME=/tmp XDG_CONFIG_HOME=/tmp

COPY Caddyfile.prod /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
  CMD wget -qO /dev/null http://127.0.0.1:8080 || exit 1

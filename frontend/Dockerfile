# Production image: build the SPA, serve it with a minimal static server.
# The edge Caddy (TLS, routing) reverse-proxies to this container.
FROM node:24-alpine@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd AS build

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

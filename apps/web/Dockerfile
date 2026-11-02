# Web image: the SPA served by nginx, which proxies the API paths to the API Service.
# Build from the repository root: docker build -f apps/web/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /src
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/
COPY packages/api-client/package.json packages/api-client/
RUN pnpm install --frozen-lockfile --filter @sprint-boot/web...
COPY assets assets
COPY packages/api-client packages/api-client
COPY apps/web apps/web
RUN pnpm --filter @sprint-boot/web build

FROM nginxinc/nginx-unprivileged:alpine
# worker_processes auto starts one worker per node core, which can exceed the memory limit on a big node.
RUN sed -i "s/^worker_processes .*;/worker_processes 2;/" /etc/nginx/nginx.conf \
    && grep -q "^worker_processes 2;" /etc/nginx/nginx.conf
COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/apps/web/dist /usr/share/nginx/html
EXPOSE 8080

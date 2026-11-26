FROM node:24-alpine AS build
WORKDIR /app
COPY frontend/package*.json ./
RUN npm ci --no-audit --no-fund
COPY frontend .
RUN npm run build

FROM nginx:1.29-alpine
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1
COPY infrastructure/docker/nginx.conf /etc/nginx/templates/default.conf.template
COPY --chmod=755 infrastructure/docker/wait-for-api.sh /docker-entrypoint.d/40-wait-for-api.sh
COPY --from=build /app/dist/ietp-dashboard/browser /usr/share/nginx/html
EXPOSE 80

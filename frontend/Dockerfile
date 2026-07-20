FROM node:24-alpine AS build
WORKDIR /app

COPY frontend/package*.json ./
# Vitest / Angular peer tree needs legacy peer resolution in this project.
RUN npm ci --legacy-peer-deps

COPY frontend/ .
RUN npm run build

FROM nginx:1.27-alpine
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
EXPOSE 80

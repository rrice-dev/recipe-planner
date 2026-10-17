# Stage 1: build Angular
FROM node:26-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npx ng build --configuration production

# Stage 2: serve the built files with nginx
FROM nginx:alpine
# Where /api/ requests go and the Host name sent with them.
# These defaults match docker compose; Azure overrides both.
ENV API_UPSTREAM=http://api:8080 \
    API_HOST=api

# The nginx image fills in ${...} placeholders in templates/ when the container starts
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/recipe-planner-web/browser /usr/share/nginx/html

EXPOSE 80
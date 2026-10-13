# Stage 1: build Angular
FROM node:26-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npx ng build --configuration production

# Stage 2: serve the built files with nginx
FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/recipe-planner-web/browser /usr/share/nginx/html

EXPOSE 80
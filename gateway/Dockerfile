FROM nginx:1.31.6-alpine3.24

# Apply Alpine fixes published after the upstream image was built.
RUN apk upgrade --no-cache

COPY nginx.conf /etc/nginx/templates/default.conf.template
EXPOSE 8080

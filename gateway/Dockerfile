FROM nginx:1.31.6-alpine3.24

# Apply Alpine fixes published after the upstream image was built.
RUN apk upgrade --no-cache

# The Nginx entrypoint substitutes only these deployment settings; Nginx $variables remain intact.
ENV NGINX_ENVSUBST_FILTER=MARKETFLOW_ \
    MARKETFLOW_CORS_ORIGIN=http://localhost:5173 \
    MARKETFLOW_DNS_RESOLVER=127.0.0.11 \
    MARKETFLOW_IDENTITY_URL=http://identity-api:8080 \
    MARKETFLOW_CATALOG_URL=http://catalog-api:8080 \
    MARKETFLOW_ORDER_URL=http://order-api:8080 \
    MARKETFLOW_REPORTING_URL=http://reporting-api:8080

COPY nginx.conf /etc/nginx/templates/default.conf.template
EXPOSE 8080

FROM alpine:3.17

# Install curl and bash
RUN apk add --no-cache curl bash

# Copy configuration files
COPY elasticsearch-index-template.json /elasticsearch-index-template.json
COPY configure-elasticsearch.sh /configure-elasticsearch.sh

# Make script executable
RUN chmod +x /configure-elasticsearch.sh

# Set script as entrypoint
ENTRYPOINT ["/configure-elasticsearch.sh"]

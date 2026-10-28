# How to update Swagger Documentation

The Swagger UI shows `swagger_src/swagger-ui/swagger-endpoints.yaml`. Syndicate only uploads this file to the
Swagger UI S3 bucket on deploy (`swagger_src/swagger-ui/swagger_ui_config.json`); it is not built from the code,
so it has to be regenerated whenever routes change.

1. Regenerate the spec locally (no AWS access needed, PyYAML comes with the Syndicate virtualenv):
```bash
syndicate_venv/bin/python scripts/swagger/generate_swagger.py
```
   - Paths, methods and auth are taken from `deployment_resources.json`, descriptions and JSON examples
     from `docs/ApiRoutes.md` — keep both up to date when adding an endpoint.
   - The server URL defaults to the `develop` stage of the deployed API; pass `--api-url <url>` for another one.
   - Operations marked `x-generated: true` are rebuilt on every run. To describe an operation by hand,
     remove that flag from it — hand-written operations and `components` are preserved.
   - Operations that are not in API Gateway are removed from the spec (the script lists them).
   - `--check` only reports whether the spec is up to date (exit code 1 if not).
2. Deploy:
```bash
export SDCT_CONF="$(pwd)/.syndicate-config-restaurant-backend-app"
syndicate build && syndicate update
```

## Alternative: export from the deployed API Gateway

1. Deploy application using AWS Syndicate
2. Export schema using:
```bash
syndicate export -rt api_gateway
```
3. Rename exported schema file in the `export` folder to `specs.json` and put in the root folder of backend application.
4. Generate swagger UI resource using:
```bash
syndicate generate swagger-ui --name swagger-ui --target-bucket swaggerui --path-to-spec specs.json
```
5. In the file `swagger_src/swagger_ui_config.json` change `target_bucket` value:
```patch
-"target_bucket": "swaggerui"
+"target_bucket": "${swagger_ui_hoster_bucket_name}"
```
6. Redeploy application using `syndicate deploy`.

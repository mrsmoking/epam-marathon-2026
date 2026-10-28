#!/usr/bin/env python3
"""Regenerate the Swagger UI spec (swagger_src/swagger-ui/swagger-endpoints.yaml) from the repository.

Sources:
  * deployment_resources.json  - the list of API Gateway paths, methods and their authorization
                                 (the source of truth for what is actually deployed);
  * docs/ApiRoutes.md          - descriptions and JSON examples for each route;
  * the current spec file      - hand-written operations (without `x-generated: true`) and components
                                 are kept as they are, so manual improvements survive re-generation.
                                 Generated operations are rebuilt on every run.

Operations that exist in the spec but not in API Gateway are removed (and listed in the output).
No AWS access is needed. Requires PyYAML, e.g. the Syndicate virtualenv:

    syndicate_venv/bin/python scripts/swagger/generate_swagger.py
    syndicate_venv/bin/python scripts/swagger/generate_swagger.py --api-url https://<api-id>.execute-api.us-west-2.amazonaws.com/develop
    syndicate_venv/bin/python scripts/swagger/generate_swagger.py --check   # exit 1 if the spec is out of date

Then deploy with `syndicate build && syndicate update` (the swagger_ui resource uploads the spec to S3).
"""

import argparse
import json
import re
import sys
from pathlib import Path

import yaml

APP_DIR = Path(__file__).resolve().parents[2]
DEFAULT_RESOURCES = APP_DIR / "deployment_resources.json"
DEFAULT_DOCS = APP_DIR / "docs" / "ApiRoutes.md"
DEFAULT_SPEC = APP_DIR / "swagger_src" / "swagger-ui" / "swagger-endpoints.yaml"
DEFAULT_API_URL = "https://er3rix88rb.execute-api.us-west-2.amazonaws.com/develop"

METHODS = ("get", "post", "put", "patch", "delete")
BODY_METHODS = ("post", "put", "patch")
# Operations carrying this flag are rebuilt on every run; remove it from an operation to edit it by hand.
GENERATED_MARK = "x-generated"
PARAM_RE = re.compile(r"\{([^}]+)\}")
DOC_HEADING_RE = re.compile(r"^###\s+`(\w+)`\s+(\S+)")

# Tag per first path segment; guest routes are split the same way as in the hand-written part of the spec.
TAGS = {
    "register": "Auth",
    "signin": "Auth",
    "me": "Profile",
    "users": "Users",
    "waiters": "Waiters",
    "locations": "Locations",
    "dishes": "Dishes",
    "tables": "Tables",
    "available-tables": "Tables",
    "reservations": "Reservations",
    "orders": "Orders",
    "feedback": "Feedback",
    "reports": "Reports",
    "notifications": "Notifications",
}

BEARER_DESCRIPTION = (
    "`Authorization: Bearer <token>`.\n\n"
    "* Routes protected by the Cognito authorizer expect the **idToken** returned by `POST /signin`.\n"
    "* Guest routes `/guest/sessions/{guestSessionId}/...` expect the **guest session token** "
    "returned by `POST /guest/sessions`."
)


def normalize(path):
    return PARAM_RE.sub("{}", path.split("?")[0].rstrip("/"))


def tag_for(path):
    segments = path.strip("/").split("/")
    if segments[0] == "guest":
        return "Guest Sessions" if segments[1] == "sessions" else "Group Reservations"
    return TAGS.get(segments[0], segments[0].replace("-", " ").title())


def load_gateway_routes(resources_file):
    resources = json.loads(resources_file.read_text(encoding="utf-8"))
    gateways = [r for r in resources.values() if r.get("resource_type") == "api_gateway"]
    if len(gateways) != 1:
        sys.exit(f"Expected exactly one api_gateway resource in {resources_file}, found {len(gateways)}")
    routes = {}
    for path, methods in gateways[0]["resources"].items():
        for method, config in methods.items():
            if method.lower() in METHODS:
                routes[(path, method.lower())] = config.get("authorization_type", "NONE")
    return routes


def load_docs(docs_file):
    """Return {(normalized path, method): section markdown} from docs/ApiRoutes.md."""
    sections = {}
    key, lines = None, []
    for line in docs_file.read_text(encoding="utf-8").splitlines() + ["#"]:
        if line.startswith("#"):
            if key:
                sections[key] = "\n".join(lines).strip()
            match = DOC_HEADING_RE.match(line)
            key = (normalize(match.group(2)), match.group(1).lower()) if match else None
            lines = []
        elif key:
            lines.append(line)
    return sections


def parse_doc(markdown):
    """Split a docs section into (request examples, response examples, remaining text).

    JSON blocks before the word "Response" are request examples, the ones after it are response examples.
    Blocks that parse as JSON become examples and are dropped from the text; the rest of the text is kept.
    """
    request, response, text = [], [], []
    seen_response = False
    for part in re.split(r"(```(?:json)?\n.*?```)", markdown, flags=re.S):
        if part.startswith("```"):
            body = re.sub(r"^```(?:json)?\n|```$", "", part)
            body = re.sub(r",(\s*[}\]])", r"\1", body)  # docs sometimes have trailing commas
            try:
                example = json.loads(body)
            except json.JSONDecodeError:
                text.append(part)
                continue
            (response if seen_response else request).append(example)
        else:
            if re.search(r"response", part, re.I):
                seen_response = True
            text.append(part)
    # Bare labels like "Request body:" or "Response: `200 OK`" only introduced the JSON blocks moved to examples.
    lines = [line for line in "".join(text).splitlines()
             if not re.fullmatch(r"\s*(Request( body)?:?|Response:?\s*`?(\d{3}[^`]*)?`?)\s*", line, re.I)]
    remaining = re.sub(r"\n{3,}", "\n\n", "\n".join(lines)).strip()
    return request, response, remaining


def schema_from_example(example):
    if isinstance(example, list):
        return {"type": "array", "items": schema_from_example(example[0]) if example else {}}
    if isinstance(example, dict):
        return {"type": "object"}
    if isinstance(example, bool):
        return {"type": "boolean"}
    if isinstance(example, (int, float)):
        return {"type": "number"}
    return {"type": "string"}


def build_operation(path, method, auth, doc):
    secured = auth == "cognito_authorizer" or path.startswith("/guest/sessions/{")
    operation = {
        GENERATED_MARK: True,
        "tags": [tag_for(path)],
        "summary": f"{method.upper()} {path}",
    }
    request_examples, response_examples, description = parse_doc(doc or "")
    if description:
        operation["description"] = description
    params = PARAM_RE.findall(path)
    if params:
        operation["parameters"] = [
            {"name": name, "in": "path", "required": True, "schema": {"type": "string"}} for name in params
        ]
    operation["security"] = [{"bearerAuth": []}] if secured else []

    if method in BODY_METHODS and request_examples:
        operation["requestBody"] = {
            "required": True,
            "content": {"application/json": {
                "schema": schema_from_example(request_examples[0]),
                "example": request_examples[0],
            }},
        }

    success = {"description": "Success"}
    if response_examples:
        success["content"] = {"application/json": {
            "schema": schema_from_example(response_examples[0]),
            "example": response_examples[0],
        }}
    responses = {"200": success}
    if secured:
        responses["401"] = {"description": "Missing or invalid token"}
    responses["500"] = {"description": "Internal server error"}
    operation["responses"] = responses
    return operation


def build_spec(base, routes, docs, api_url):
    old_paths = base.get("paths") or {}
    kept, generated, removed = [], [], []

    paths = {}
    for (path, method), auth in sorted(routes.items(), key=lambda r: (tag_for(r[0][0]), r[0][0], METHODS.index(r[0][1]))):
        existing = (old_paths.get(path) or {}).get(method)
        if existing and not existing.get(GENERATED_MARK):
            paths.setdefault(path, {})[method] = existing
            kept.append((method, path))
        else:
            doc = docs.get((normalize(path), method))
            paths.setdefault(path, {})[method] = build_operation(path, method, auth, doc)
            generated.append((method, path, doc is not None))

    for path, methods in old_paths.items():
        for method in methods:
            if method in METHODS and (path, method) not in routes:
                removed.append((method, path))

    used_tags = []
    for operations in paths.values():
        for operation in operations.values():
            for tag in operation.get("tags", []):
                if tag not in used_tags:
                    used_tags.append(tag)
    old_tags = {t["name"]: t for t in base.get("tags") or []}

    components = base.get("components") or {}
    schemes = components.setdefault("securitySchemes", {})
    schemes["bearerAuth"] = {
        "type": "http", "scheme": "bearer", "bearerFormat": "JWT", "description": BEARER_DESCRIPTION,
    }

    spec = {
        "openapi": base.get("openapi", "3.0.0"),
        "info": {
            "title": "Restaurant Backend API",
            "description": (
                "REST API of the restaurant backend (api-handler lambda behind API Gateway).\n\n"
                "Generated by `scripts/swagger/generate_swagger.py` from `deployment_resources.json` "
                "and `docs/ApiRoutes.md`. Sign in with `POST /signin` and use the returned idToken in **Authorize**."
            ),
            "version": (base.get("info") or {}).get("version", "1.0.0"),
        },
        "servers": [{"url": api_url, "description": "develop stage"}],
        "tags": [old_tags.get(tag, {"name": tag}) for tag in used_tags],
        "paths": paths,
        "components": components,
    }
    return spec, kept, generated, removed


def dump(spec):
    class Dumper(yaml.SafeDumper):
        pass

    def str_representer(dumper, value):
        style = "|" if "\n" in value else None
        return dumper.represent_scalar("tag:yaml.org,2002:str", value, style=style)

    Dumper.add_representer(str, str_representer)
    text = yaml.dump(spec, Dumper=Dumper, sort_keys=False, allow_unicode=True, width=120)
    return text.replace("\r\n", "\n").replace("\n", "\r\n")  # the repository keeps this file with CRLF


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--api-url", default=DEFAULT_API_URL, help=f"server URL in the spec (default: {DEFAULT_API_URL})")
    parser.add_argument("--resources", type=Path, default=DEFAULT_RESOURCES)
    parser.add_argument("--docs", type=Path, default=DEFAULT_DOCS)
    parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC, help="spec file to read and overwrite")
    parser.add_argument("--check", action="store_true", help="do not write; exit 1 if the spec would change")
    args = parser.parse_args()

    base = yaml.safe_load(args.spec.read_text(encoding="utf-8")) if args.spec.exists() else {}
    routes = load_gateway_routes(args.resources)
    docs = load_docs(args.docs)
    spec, kept, generated, removed = build_spec(base, routes, docs, args.api_url)
    text = dump(spec)

    print(f"API Gateway operations: {len(routes)}")
    print(f"  kept from the existing spec: {len(kept)}")
    print(f"  generated: {len(generated)} ({sum(1 for g in generated if g[2])} with docs from ApiRoutes.md)")
    for method, path, has_doc in generated:
        if not has_doc:
            print(f"    ! no docs section for {method.upper()} {path}")
    if removed:
        print(f"  removed (not in API Gateway): {len(removed)}")
        for method, path in removed:
            print(f"    - {method.upper()} {path}")

    current = args.spec.read_bytes().decode("utf-8") if args.spec.exists() else ""
    if args.check:
        print("Spec is up to date." if current == text else "Spec is OUT OF DATE.")
        return 0 if current == text else 1
    args.spec.write_bytes(text.encode("utf-8"))
    print(f"Written {args.spec.relative_to(APP_DIR)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Seed a deployed restaurant backend with demo data through its public REST API.

Creates locations (with tables), dishes linked to locations, and users
(customers and waiters) described in seed-data.json. Safe to re-run:
existing locations (by address), dishes (by name), users and waiters are skipped.

Usage:
    python3 seed.py --api-url https://<api-id>.execute-api.us-west-2.amazonaws.com/develop
    BACKEND_URL=https://... python3 seed.py --with-images
    python3 seed.py --api-url https://... --dry-run

Only the Python standard library is used. --with-images resizes photos from the
frontend assets with macOS `sips` (or Pillow, if installed) to fit the Lambda payload limit.
"""

import argparse
import base64
import json
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_DATA = SCRIPT_DIR / "seed-data.json"
DEFAULT_IMAGES_DIR = SCRIPT_DIR.parents[3] / "frontend" / "restaurant-frontend-app" / "src" / "assets" / "img"
MAX_IMAGE_SIDE = 1280
MAX_BASE64_BYTES = 4 * 1024 * 1024  # synchronous Lambda payload limit is 6 MB


class ApiError(Exception):
    def __init__(self, status, body):
        super().__init__(f"HTTP {status}: {body}")
        self.status = status
        self.body = body


class Api:
    def __init__(self, base_url, dry_run):
        self.base_url = base_url.rstrip("/")
        self.dry_run = dry_run
        self._fake_id = 0

    def request(self, method, path, body=None):
        if self.dry_run and method != "GET":
            shown = {k: (f"<{len(v)} chars>" if k == "imageBase64" and v else v) for k, v in (body or {}).items()}
            print(f"    [dry-run] {method} {path} {json.dumps(shown, ensure_ascii=False)}")
            self._fake_id += 1
            return {"id": f"dry-run-{self._fake_id}"}

        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(
            self.base_url + path,
            data=data,
            method=method,
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                raw = resp.read().decode()
        except urllib.error.HTTPError as err:
            raise ApiError(err.code, err.read().decode(errors="replace")) from None
        return json.loads(raw) if raw else None


def encode_image(images_dir, relative_path):
    source = images_dir / relative_path
    if not source.is_file():
        print(f"    ! image not found, skipped: {source}")
        return None

    with tempfile.TemporaryDirectory() as tmp:
        target = Path(tmp) / "image.jpg"
        if shutil.which("sips"):
            subprocess.run(
                ["sips", "-Z", str(MAX_IMAGE_SIDE), "-s", "format", "jpeg", "-s", "formatOptions", "70",
                 str(source), "--out", str(target)],
                check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            )
        else:
            try:
                from PIL import Image
            except ImportError:
                print("    ! neither `sips` nor Pillow is available, image skipped")
                return None
            with Image.open(source) as img:
                img.thumbnail((MAX_IMAGE_SIDE, MAX_IMAGE_SIDE))
                img.convert("RGB").save(target, "JPEG", quality=70)

        encoded = base64.b64encode(target.read_bytes()).decode()

    if len(encoded) > MAX_BASE64_BYTES:
        print(f"    ! image still too large after resize ({len(encoded)} bytes), skipped")
        return None
    return "data:image/jpeg;base64," + encoded


def seed_locations(api, data, images_dir, with_images):
    print("Locations")
    existing = {loc["address"]: loc["id"] for loc in api.request("GET", "/locations") or []}
    ids = {}
    for loc in data["locations"]:
        if loc["address"] in existing:
            ids[loc["key"]] = existing[loc["address"]]
            print(f"  = {loc['address']} (exists, tables skipped)")
            continue

        body = {
            "address": loc["address"],
            "description": loc["description"],
            "totalCapacity": loc["totalCapacity"],
            "averageOccupancy": loc["averageOccupancy"],
            "rating": loc["rating"],
            "imageBase64": encode_image(images_dir, loc["image"]) if with_images and loc.get("image") else None,
        }
        created = api.request("POST", "/locations", body)
        ids[loc["key"]] = created["id"]
        print(f"  + {loc['address']} -> {created['id']}")

        for table in loc.get("tables", []):
            api.request("POST", "/tables", {"locationId": created["id"], **table})
        print(f"    + {len(loc.get('tables', []))} tables")
    return ids


def seed_dishes(api, data, location_ids, images_dir, with_images):
    print("Dishes")
    existing = {dish["name"] for dish in api.request("GET", "/dishes") or []}
    for dish in data["dishes"]:
        if dish["name"] in existing:
            print(f"  = {dish['name']} (exists)")
            continue

        body = {k: v for k, v in dish.items() if k not in ("image", "locations")}
        body["locationIds"] = [location_ids[key] for key in dish["locations"]]
        body["imageBase64"] = encode_image(images_dir, dish["image"]) if with_images and dish.get("image") else None
        created = api.request("POST", "/dishes", body)
        print(f"  + {dish['name']} -> {created['id']}")


def seed_users(api, data, location_ids):
    print("Users")
    for user in data["users"]:
        body = {
            "firstName": user["firstName"],
            "lastName": user["lastName"],
            "email": user["email"],
            "password": user["password"],
            "confirmPassword": user["password"],
        }
        try:
            api.request("POST", "/register", body)
            print(f"  + {user['email']}")
        except ApiError as err:
            if "exist" not in err.body.lower():
                raise
            print(f"  = {user['email']} (exists)")

        if "waiterAt" in user:
            try:
                api.request("POST", "/waiters", {"email": user["email"], "locationId": location_ids[user["waiterAt"]]})
                print(f"    + waiter at {user['waiterAt']}")
            except ApiError as err:
                if err.status != 409:
                    raise
                print(f"    = already a waiter")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--api-url", default=os.environ.get("BACKEND_URL"),
                        help="API Gateway base URL incl. stage (default: $BACKEND_URL)")
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA, help="seed data file")
    parser.add_argument("--images-dir", type=Path, default=DEFAULT_IMAGES_DIR, help="base directory for image paths")
    parser.add_argument("--with-images", action="store_true", help="upload location and dish photos (resized)")
    parser.add_argument("--dry-run", action="store_true", help="print write requests instead of sending them")
    args = parser.parse_args()

    if not args.api_url:
        parser.error("--api-url or BACKEND_URL is required")

    data = json.loads(args.data.read_text(encoding="utf-8"))
    api = Api(args.api_url, args.dry_run)
    print(f"Seeding {api.base_url}{' (dry run)' if args.dry_run else ''}")

    try:
        location_ids = seed_locations(api, data, args.images_dir, args.with_images)
        seed_dishes(api, data, location_ids, args.images_dir, args.with_images)
        seed_users(api, data, location_ids)
    except ApiError as err:
        print(f"\nFAILED: {err}", file=sys.stderr)
        return 1

    print("\nDone.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

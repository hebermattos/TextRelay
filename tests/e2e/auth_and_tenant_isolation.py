import json
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from uuid import uuid4

BASE_URL = "http://localhost:8080"


def request(path, payload=None, token=None, method=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(
        BASE_URL + path,
        data=None if payload is None else json.dumps(payload).encode(),
        headers=headers,
        method=method,
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def wait_until_healthy():
    for _ in range(30):
        try:
            if request("/health")[0] == 200:
                return
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            pass
        time.sleep(2)
    raise RuntimeError("API did not become healthy")


def expect(path, expected, *, token=None, payload=None, method=None):
    status, body = request(path, payload, token, method)
    assert status == expected, f"{method or ('POST' if payload is not None else 'GET')} {path}: expected {expected}, got {status}"
    return json.loads(body) if body and 200 <= status < 300 else None


def provision_tenant(admin_token):
    client_id = f"e2e-{uuid4().hex}"
    tenant = expect("/api/v1/admin/tenants", 201, token=admin_token, payload={
        "name": client_id,
        "clientId": client_id,
    })
    auth = expect("/api/v1/auth/token", 200, payload={
        "clientId": tenant["client_id"],
        "clientSecret": tenant["client_secret"],
    })
    return tenant["tenant_id"], auth["access_token"]


def verify_tenant_isolation(admin_token):
    tenant_a, token_a = provision_tenant(admin_token)
    tenant_b, token_b = provision_tenant(admin_token)
    assert tenant_a != tenant_b

    messages_path = "/api/v1/messages"
    # Future UTC wall time for the newly provisioned tenants. No provider call is needed.
    tomorrow = (datetime.now(timezone.utc) + timedelta(days=1)).replace(tzinfo=None)
    message = expect(messages_path, 202, token=token_a, payload={
        "to": "+12025550101",
        "body": "Tenant isolation test",
        "provider": "Twilio",
        "scheduledAt": tomorrow.isoformat(timespec="seconds"),
    })
    message_path = f'{messages_path}/{message["id"]}'
    assert expect(message_path, 200, token=token_a)["tenantId"] == tenant_a
    assert isinstance(expect(f"{message_path}/status-history", 200, token=token_a), list)
    expect(message_path, 404, token=token_b)
    expect(f"{message_path}/status-history", 404, token=token_b)
    assert message["id"] in {row["id"] for row in expect(messages_path, 200, token=token_a)}
    assert expect(messages_path, 200, token=token_b) == []

    # A successful API login creates an audit row for each tenant.
    logs_a = expect("/api/v1/logs", 200, token=token_a)
    logs_b = expect("/api/v1/logs", 200, token=token_b)
    assert logs_a and logs_b, "Expected activity for both tenants"
    assert {row["id"] for row in logs_a}.isdisjoint(row["id"] for row in logs_b), "Shared audit rows"

    # Messages expose read endpoints only. Exercise write isolation on a tenant-owned template.
    template = expect("/api/v1/templates", 201, token=token_a, payload={
        "name": "Isolation test",
        "body": "Original template",
    })
    template_path = f'/api/v1/templates/{template["id"]}'
    expect(template_path, 404, token=token_b, method="PUT", payload={
        "name": "Isolation test",
        "body": "Cross-tenant update",
    })
    expect(template_path, 404, token=token_b, method="DELETE")
    assert expect(template_path, 200, token=token_a)["body"] == "Original template"


def main():
    wait_until_healthy()
    assert request("/api/v1/admin/auth/token", {"key": "local-admin-key-change-me"})[0] == 400
    assert request("/api/v1/admin/auth/token", {"username": "platform"})[0] == 400
    assert request("/api/v1/admin/auth/token", {"username": "platform", "password": "x" * 129})[0] == 400
    assert request("/api/v1/admin/auth/token", {"username": "platform", "password": "wrong"})[0] == 401

    status, body = request("/api/v1/admin/auth/token", {"username": "PLATFORM", "password": "platform"})
    assert status == 200, "Administrator login failed"
    token = json.loads(body)["access_token"]
    assert request("/api/v1/admin/tenants", token=token)[0] == 200
    assert request("/api/v1/overview", token=token)[0] == 403
    verify_tenant_isolation(token)
    print("Administrator login and isolation between two tenants verified.")


if __name__ == "__main__":
    main()

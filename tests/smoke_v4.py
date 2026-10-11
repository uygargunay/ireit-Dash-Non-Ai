"""V4 HTTP integration checks. Starts and stops its own server; no third-party packages."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src/IreiMvp.Web"
KEY = "local-integration-key"
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    PORT = sock.getsockname()[1]
BASE = f"http://127.0.0.1:{PORT}"
ENV = dict(os.environ, Irei__AdminKey=KEY, ASPNETCORE_ENVIRONMENT="Development")

def request(path, body=None, method="GET", authorized=True, content_type="application/json"):
    headers = {"X-Admin-Key": KEY} if authorized else {}
    if body is not None:
        headers["Content-Type"] = content_type
    req = urllib.request.Request(BASE + path, body, headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b"{}")

def check(value, label):
    if not value:
        raise AssertionError(label)
    print("PASS:", label)

with tempfile.TemporaryDirectory(prefix="irei-v4-http-") as tmp:
    log_path = Path(tmp) / "server.log"
    with log_path.open("w") as log:
        configuration = os.environ.get("V4_CONFIGURATION", "Debug")
        server = subprocess.Popen([os.environ.get("DOTNET", "dotnet"), str(WEB / f"bin/{configuration}/net8.0/IreiMvp.Web.dll"),
                                   "--urls", BASE, "--contentRoot", str(WEB), "--Irei:StoragePath", str(Path(tmp) / "storage")],
                                  cwd=WEB, env=ENV, stdout=log, stderr=log)
        assessment_id = None
        try:
            for _ in range(100):
                if server.poll() is not None:
                    raise RuntimeError(log_path.read_text())
                try:
                    if request("/health")[0] == 200:
                        break
                except OSError:
                    time.sleep(.1)
            check(request("/api/v4/assessments/unknown", authorized=False)[0] == 401, "Private V4 records require access key")
            file = WEB / "Data/Examples/IREI_Stage1_V4.2_NGOB_Real_Data_Test.xlsx"
            boundary = "irei-v4-boundary"
            body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="fixture.xlsx"\r\n'
                    'Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\r\n\r\n').encode() + file.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
            status, imported = request("/api/v4/assessments", body, "POST", content_type="multipart/form-data; boundary=" + boundary)
            check(status == 200, "HTTP workbook import")
            assessment_id = imported["id"]
            base = f"/api/v4/assessments/{assessment_id}"
            for p in range(1, 8):
                status, view = request(base + f"/pages/P{p}")
                check(status == 200 and view["page"] == f"P{p}", f"HTTP P{p} reusable view")
            status, first = request(base + "/pages/P1")
            metric = next(m for m in first["currentMetrics"] if m["code"] == "DSCR")
            status, evidence = request(base + "/metrics/" + metric["id"] + "/evidence")
            check(status == 200 and evidence["mappedFacts"] and evidence["sourceChecksum"], "Metric source-to-calculation drill-through")
            status, _ = request(base + "/configuration", json.dumps({"currentYear": "2025-2026", "expectedVersion": 1}).encode(), "PUT")
            check(status == 200, "HTTP current year update")
            _, changed = request(base + "/pages/P1")
            check(changed["context"]["currentYear"] == "2025-2026" and changed["baseline"] == first["baseline"], "Year selection preserves baseline")
            check(request(base + "/approve", b"{}", "POST")[0] == 400, "Pilot approval blocked")
            check(request(base + "/reports/release", b"{}", "POST")[0] == 400, "External release separately blocked")
            _, result = request(base + "/options/test", json.dumps({"year": "2029-2030", "case": "Without placeholder", "kind": "Bridge", "amount": 3376096.971565852}).encode(), "POST")
            check(abs(result["coverageAfter"] - .7541358950360945) < .000001 and result["residualCashShortfall"] < .01, "HTTP bridge funds cash without improving coverage")
            # Optional browser verification shares this server/network lifetime.
            if "V4_BROWSER_TEST" in os.environ:
                subprocess.run(["node", str(ROOT / "tests/browser_v4.cjs"), BASE, assessment_id, KEY, os.environ["V4_BROWSER_TEST"]], check=True)
            print("V4 HTTP integration checks passed.")
        finally:
            server.terminate()
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.kill()
            if assessment_id:
                import shutil
                shutil.rmtree(WEB / "App_Data/V4Assessments" / assessment_id, ignore_errors=True)

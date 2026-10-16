"""Staff spreadsheet-intake integration checks. Originals are optional external fixtures, never committed.

IREI_TEST_SERVER must be a JSON command array; defaults to dotnet run.
Usage: python tests/smoke_intake.py [original-financial.xlsx property-summary.xlsx]
"""
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
URL = "http://127.0.0.1:5091"
KEY = uuid.uuid4().hex
passed = 0


def check(value, name):
    global passed
    assert value, name
    passed += 1
    print("PASS:", name)


def request(path, data=None, status=200, raw=False, auth=True):
    headers = {"X-Admin-Key": KEY} if auth else {}
    if data is not None:
        data = json.dumps(data).encode()
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(URL + path, data=data, headers=headers)
    try:
        response = urllib.request.urlopen(req, timeout=90)
    except urllib.error.HTTPError as e:
        response = e
    body = response.read()
    assert response.status == status, (path, response.status, body[:500])
    return body if raw else json.loads(body)


def upload(batch, name, content):
    boundary = uuid.uuid4().hex
    data = (f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="{name}"\r\n'
            'Content-Type: application/octet-stream\r\n\r\n').encode() + content + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(URL + f"/api/v4/intake/batches/{batch['id']}/files", data=data,
                                 headers={"X-Admin-Key": KEY, "Content-Type": "multipart/form-data; boundary=" + boundary})
    return json.load(urllib.request.urlopen(req, timeout=90))


def create(org):
    return request("/api/v4/intake/batches", {"organizationId": org, "organizationName": org,
        "assessmentId": "ASSESS-" + org, "currency": "CAD", "currentYear": "2026-2027", "fiscalStartMonth": 4})


def review(batch, c, code, value, scope="PORTFOLIO", supersedes="", status=200):
    return request(f"/api/v4/intake/batches/{batch['id']}/candidates/{c['id']}/review", {
        "state": "Accepted", "code": code, "value": value, "scope": scope, "year": "2026-2027",
        "scenario": "Projection", "grain": "Portfolio control" if scope == "PORTFOLIO" else "Property detail",
        "certainty": "Projected", "reviewer": "Automated fixture reviewer", "rationale": "Test only: annual CAD, unit scale 1, fiscal April to March; source cache reviewed.",
        "expectedVersion": batch["version"], "supersedesId": supersedes}, status=status)


def formulas(data):
    ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        return {(n, c.attrib["r"]): c.find("s:f", ns).text for n in z.namelist() if n.startswith("xl/worksheets/") and n.endswith(".xml")
                for c in ET.fromstring(z.read(n)).findall(".//s:c", ns) if c.find("s:f", ns) is not None}


command = json.loads(os.environ.get("IREI_TEST_SERVER", '["dotnet","run","--project","src/IreiMvp.Web","--no-launch-profile","--"]'))
command += ["--contentRoot", str(ROOT / "src/IreiMvp.Web"), "--urls", URL]
log = open(os.environ.get("IREI_TEST_LOG", "/tmp/irei-intake-server.log"), "w")
server = subprocess.Popen(command, cwd=ROOT, env=dict(os.environ, Irei__IntakeEnabled="true", Irei__AdminKey=KEY), stdout=log, stderr=log)
try:
    for _ in range(100):
        try:
            urllib.request.urlopen(URL + "/health", timeout=1)
            break
        except OSError:
            time.sleep(.1)
    else:
        raise RuntimeError("Test server failed to start; inspect /tmp/irei-intake-server.log")
    request("/api/v4/intake/batches/" + uuid.uuid4().hex, status=401, raw=True, auth=False)
    check(True, "Intake denies missing key")
    b = create("SYNTHETIC-" + uuid.uuid4().hex[:8])
    source = b"Label,2026-2027\nTOTAL REVENUE,200000\nTOTAL OPERATING EXPENSES,100000\nMortgage Payments,50000\n"
    b = upload(b, "different-layout.csv", source)
    check(len(b["sources"]) == 1 and len(b["candidates"]) == 3, "CSV table proposes all three fields")
    before = b["version"]
    b = upload(b, "renamed-same-bytes.csv", source)
    check(len(b["sources"]) == 1 and b["version"] == before, "Byte-identical reupload is idempotent")
    for c in b["candidates"][:]:
        b = review(b, c, c["suggestedCode"], c["rawText"])
    commit = lambda batch: request(f"/api/v4/intake/batches/{batch['id']}/commit", {"expectedVersion": batch["version"]})
    b = commit(b)
    aid = b["assessmentRuntimeIds"][-1]
    a = request("/api/v4/assessments/" + aid)
    metric = lambda assessment, code: next(m for m in assessment["metrics"] if m["scope"] == "PORTFOLIO" and m["year"] == "2026-2027" and m["code"] == code)
    check(metric(a, "DSCR")["value"] == 2, "Original CSV to reviewed workbook to existing engine gives 2x coverage")
    check(commit(b)["assessmentRuntimeIds"] == b["assessmentRuntimeIds"], "Commit retry does not duplicate assessment")
    workbook = request(f"/api/v4/intake/batches/{b['id']}/export", raw=True)
    blank = (ROOT / "src/IreiMvp.Web/Data/Templates/IREI_Stage1_MVP_V4.2_Live_Underwriting_Template_BLANK.xlsx").read_bytes()
    check(formulas(workbook) == formulas(blank), "Every original template formula is preserved")
    manifest = request(f"/api/v4/intake/batches/{b['id']}/manifest")
    check(len(manifest) == 3, "Accepted candidates map to exact export rows")
    baseline = a["baseline"]
    old_revenue = next(c for c in b["candidates"] if c["code"] == "REVENUE")
    b = upload(b, "partial-revision.csv", b"Changed table,2026-2027\nTOTAL REVENUE,250000\n")
    changed = b["candidates"][-1]
    review(b, changed, "REVENUE", 250000, status=400)
    check(True, "Conflicting active fact cannot be accepted without explicit supersession")
    b = review(b, changed, "REVENUE", 250000, supersedes=old_revenue["id"])
    check(sum(c["state"] == "Accepted" for c in b["candidates"]) == 3, "Partial update retains other accepted fields")
    b = commit(b)
    updated = request("/api/v4/assessments/" + b["assessmentRuntimeIds"][-1])
    check(metric(updated, "DSCR")["value"] == 3, "Revised input changes engine coverage")
    check(updated["baseline"] == baseline and updated["priorRuntimeAssessmentId"] == aid, "Update preserves original baseline and assessment lineage")
    prior = request("/api/v4/assessments/" + aid)
    check(metric(prior, "DSCR")["value"] == 2, "Prior assessment remains unchanged")
    second = create("OTHER-" + uuid.uuid4().hex[:8])
    check(not second["candidates"] and not second["sources"], "Unfamiliar organization starts without fixture data")
    failed = upload(second, "broken.xlsx", b"not a workbook")
    check(failed["sources"][0]["status"].startswith("Failed") and not failed["candidates"], "Failed extraction is explicit and leaves no partial candidates")
    edge = upload(second, "zero-and-sign.csv", b'Label,2026-2027\nTOTAL REVENUE,0\nMortgage Payments,-10\n')
    zero = next(c for c in edge["candidates"] if c["suggestedCode"] == "REVENUE")
    edge = review(edge, zero, "REVENUE", 0)
    check(next(c for c in edge["candidates"] if c["id"] == zero["id"])["value"] == 0, "Explicit zero remains an accepted zero")
    negative = next(c for c in edge["candidates"] if c["suggestedCode"] == "DEBT_SERVICE")
    review(edge, negative, "DEBT_SERVICE", -10, status=400)
    check(True, "Negative reviewed debt cannot enter engine inputs")
    malformed = upload(second, "invalid-period.csv", b'Label,20230-2031\nTOTAL REVENUE,100\n')
    c = malformed["candidates"][-1]
    request(f"/api/v4/intake/batches/{malformed['id']}/candidates/{c['id']}/review", {
        "state":"Accepted", "code":"REVENUE", "value":100, "scope":"PORTFOLIO", "year":"20230-2031", "scenario":"Projection",
        "grain":"Portfolio control", "certainty":"Projected", "reviewer":"Test", "rationale":"Test only", "expectedVersion":malformed["version"]}, status=400)
    check(True, "Malformed period cannot be accepted without reviewed normalization")
    b = request(f"/api/v4/intake/batches/{b['id']}/effort", {"role": "Analyst", "task": "Review", "minutes": 12, "machineCost": 0, "costCurrency": "CAD", "note": "synthetic test; not productivity evidence"})
    check(len(b["effort"]) == 1, "Role/task time and machine cost retained separately")
    for path in sys.argv[1:]:
        original = Path(path)
        original_batch = locals().get("original_batch") or create("ORIGINAL-TEST-" + uuid.uuid4().hex[:8])
        original_batch = upload(original_batch, original.name, original.read_bytes())
        last = original_batch["sources"][-1]
        check(last["status"] == "Awaiting review", "Original file extracted: " + original.name)
        check(all(c["state"] == "Proposed" for c in original_batch["candidates"]), "Original observations are not autoapproved")
        print("ORIGINAL INVENTORY", original.name, "sheets", len(last["sheets"]), "candidates", sum(c["sourceId"] == last["id"] for c in original_batch["candidates"]))
    if "original_batch" in locals():
        check(any(c["rawYear"] == "20230-2031" for c in original_batch["candidates"]), "Malformed fiscal header retained without silent normalization")
        check(any(c["sheet"] == "JP" and c["cell"] == "A32" for c in original_batch["candidates"]), "Year-only Jessica source note retains exact locator")
        check(any(c["cell"] == "X26" and c["suggestedProperty"] == "Jessica Place" for c in original_batch["candidates"]), "Historical Jessica date remains separate source evidence")
        note = next(c for c in original_batch["candidates"] if c["sheet"] == "JP" and c["cell"] == "A32")
        review(original_batch, note, "DEBT_SERVICE", 2027, status=400)
        check(True, "Year-only mortgage note cannot become a numeric financial fact")
        b = original_batch
        sheet = b["sources"][0]["sheets"][0]["name"]
        controls = [c for c in b["candidates"] if c["sheet"] == sheet and c["rawYear"] == "2026-2027" and c["suggestedCode"] in ("REVENUE", "OPEX_SOCIETY", "OPEX_BUILDINGS", "DEBT_PRINCIPAL", "DEBT_INTEREST", "RESERVE_CLOSING")]
        check(len(controls) == 6, "Six original portfolio controls proposed by labels and period headers")
        for c in controls:
            b = review(b, c, c["suggestedCode"], c["rawText"])
        b = commit(b)
        a = request("/api/v4/assessments/" + b["assessmentRuntimeIds"][-1])
        check(abs(metric(a, "REVENUE")["value"] - 12696486.415908122) < .01, "Original-source FY27 revenue parity")
        check(abs(metric(a, "OPEX")["value"] - 8614110.832749803) < .01, "Original-source FY27 operating-cost parity")
        check(abs(metric(a, "DSCR")["value"] - 1.5755698693070097) < .000001, "Original-source FY27 coverage parity")
    print("INTAKE:", passed, "checks passed. Staff-only bounded demo; no multi-tenant or general-layout acceptance claimed.")
finally:
    server.terminate()
    try:
        server.wait(timeout=5)
    except subprocess.TimeoutExpired:
        server.kill()
    log.close()

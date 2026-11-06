"""Synthetic Phase 2 portal integration tests. No client documents or credentials are committed."""
import base64
import csv
import hashlib
import hmac
import http.cookiejar
import io
import json
import os
from pathlib import Path
import socket
import socketserver
import ssl
import struct
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
DOTNET = os.environ.get("DOTNET_EXE", "dotnet")
BASE = "http://127.0.0.1:5093"
PASSWORD = "Synthetic-only-" + uuid.uuid4().hex
TOTP_SECRET = base64.b32encode(os.urandom(20)).decode()
passed = 0


def check(value, name):
    global passed
    assert value, name
    passed += 1
    print("PASS:", name, flush=True)


def totp():
    digest = hmac.new(base64.b32decode(TOTP_SECRET), struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    start = digest[-1] & 15
    return str((struct.unpack(">I", digest[start:start + 4])[0] & 0x7fffffff) % 1000000).zfill(6)


class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))
        self.csrf = ""

    def call(self, path, data=None, method=None, status=200, raw=False, csrf=True):
        headers = {}
        if csrf:
            headers["X-CSRF-Token"] = self.csrf
        if isinstance(data, bytes):
            headers["Content-Type"] = "application/octet-stream"
        elif data is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(data).encode()
        request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
        try:
            response = self.opener.open(request, timeout=60)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        assert response.status == status, (path, response.status, body[:700])
        return body if raw else (json.loads(body) if body else {})

    def session(self):
        s = self.call("/api/evidence/session")
        self.csrf = s["csrf"]
        return s


def new_tenant(admin, name):
    return admin.call("/api/evidence/tenants", {"name": name, "pilotScope": "Synthetic representative-sample pilot", "reportingCutoff": "2026-10-31", "scopeIds": ["organization", "department-b"], "representativeSample": True})


def invite(admin, tenant, email, role="ClientContributor", scopes=None):
    invitation = admin.call("/api/evidence/invitations", {"tenantId": tenant["id"], "email": email, "role": role, "scopes": scopes or ["organization"]})
    c = Client()
    c.session()
    c.call("/api/evidence/accept-invitation", {"token": invitation["token"], "password": PASSWORD, "totpSecret": "", "totp": ""})
    c.session()
    return c, invitation


def prepare(c, tenant, files, category="02", scope="organization", existing=None, period="FY2026"):
    return c.call("/api/evidence/uploads/prepare", {"tenantId": tenant["id"], "scopeId": scope, "categoryIds": [category], "propertyIds": [], "reportingPeriod": period,
        "coverage": "Partial", "confirmedNoPersonalData": True, "existingDocumentId": existing, "files": [{"name": name, "size": len(data)} for name, data in files]})


def upload(c, tenant, name, data, category="02", scope="organization", existing=None):
    reservation = prepare(c, tenant, [(name, data)], category, scope, existing)
    return c.call(f"/api/evidence/uploads/{reservation['id']}/0", data)


def workspace(c, tenant):
    return c.call("/api/evidence/tenants/" + tenant["id"])


def xlsx():
    mem = io.BytesIO()
    with zipfile.ZipFile(mem, "w") as z:
        z.writestr("[Content_Types].xml", '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')
        z.writestr("xl/workbook.xml", '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Source" sheetId="1" r:id="rId1"/></sheets></workbook>')
        z.writestr("xl/_rels/workbook.xml.rels", '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"/></Relationships>')
        z.writestr("xl/worksheets/sheet1.xml", '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Fiscal year</t></is></c><c r="B1" t="inlineStr"><is><t>2026-2027</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>TOTAL REVENUE</t></is></c><c r="B2"><v>100</v></c></row></sheetData></worksheet>')
    return mem.getvalue()


def docx():
    mem = io.BytesIO()
    with zipfile.ZipFile(mem, "w") as z:
        z.writestr("[Content_Types].xml", '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')
        z.writestr("word/document.xml", '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Synthetic aggregate policy</w:t></w:r></w:p></w:body></w:document>')
    return mem.getvalue()


def fact(admin, tenant, version, value, previous=None):
    return admin.call(f"/api/evidence/tenants/{tenant['id']}/facts", {"documentVersionId": version, "metricCode": "CASH_BALANCE", "value": value, "unit": "Currency", "currency": "CAD",
        "period": "FY2026", "asOfDate": "2026-10-31", "basis": "Management YTD", "locator": "page 1", "certainty": "Client-reported", "reason": "Synthetic manual source review", "supersedesId": previous})


def policy(admin, tenant):
    return admin.call(f"/api/evidence/tenants/{tenant['id']}/policy", {"agreementReference": "SYNTHETIC-ONLY-AGREEMENT", "hostingLocation": "Local synthetic test environment",
        "processorsAndBackups": "No client records; local test files; ephemeral test backup protocol", "clientSourceLocation": "Synthetic fixture generator and exact-version test archive", "sourceRetentionDays": 30, "derivedRetentionDays": 730,
        "writtenAuthority": True, "securityApproved": True, "backupReviewed": True, "clientPreservesExactVersions": True}, method="PUT")


def main():
    with tempfile.TemporaryDirectory(prefix="irei-evidence-test-") as folder:
        log = open(Path(folder) / "server.log", "w")
        cmd = json.loads(os.environ.get('EVIDENCE_TEST_SERVER', json.dumps([DOTNET, str(ROOT / f"src/IreiMvp.Web/bin/{os.environ.get('EVIDENCE_CONFIGURATION','Debug')}/net8.0/IreiMvp.Web.dll")]))) + ["--contentRoot", str(ROOT / "src/IreiMvp.Web"), "--urls", BASE]
        env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", Evidence__Enabled="true", Evidence__Prototype="true", Evidence__AllowLocalHttp="true", Evidence__EncryptionKey=base64.b64encode(os.urandom(32)).decode(),
            Evidence__BootstrapEmail="admin@example.test", Evidence__BootstrapPassword=PASSWORD, Evidence__BootstrapTotpSecret=TOTP_SECRET, Evidence__StoragePath=folder,
            Evidence__FileLimitBytes="1048576", Evidence__BatchLimitBytes="4194304", Evidence__TenantQuotaBytes="6291456", Evidence__GlobalQuotaBytes="16777216")
        server = subprocess.Popen(cmd, cwd=ROOT, env=env, stdout=log, stderr=log)
        try:
            admin = Client()
            for _ in range(100):
                try:
                    admin.session()
                    break
                except OSError:
                    if server.poll() is not None:
                        raise RuntimeError(Path(folder, "server.log").read_text())
                    time.sleep(.1)
            else:
                raise RuntimeError("Portal did not start")
            admin.call("/api/evidence/login", {"email": "admin@example.test", "password": PASSWORD, "totp": ""}, status=401)
            check(True, "Administrator password alone does not bypass MFA")
            admin.call("/api/evidence/login", {"email": "admin@example.test", "password": PASSWORD, "totp": totp()})
            admin.session()
            a = new_tenant(admin, "Pilot A")
            b = new_tenant(admin, "Pilot B")
            ca, invitation = invite(admin, a, "a@example.test")
            cb, _ = invite(admin, b, "b@example.test")
            department, _ = invite(admin, a, "department@example.test", scopes=["department-b"])
            approver, _ = invite(admin, a, "approver@example.test", role="ClientApprover", scopes=["*"])
            analyst, _ = invite(admin, a, "analyst@example.test", role="IreiAnalyst")
            check(ca.session()["profile"]["tenants"][0]["id"] == a["id"], "Invitation assigns only its authorized organization")
            outsider = Client()
            outsider.call(f"/api/evidence/tenants/{a['id']}", status=401)
            cb.call(f"/api/evidence/tenants/{a['id']}", status=404)
            analyst.call(f"/api/evidence/tenants/{b['id']}", status=404)
            check(True, "Anonymous, other-client and unassigned staff cannot retrieve organization metadata")
            ca.call(f"/api/evidence/tenants/{a['id']}/ready/organization", {"state": "Submitted", "note": "test"}, csrf=False, status=403)
            check(True, "Mutations require session-bound CSRF confirmation")
            check(len(workspace(ca, a)["categories"]) == 10, "All ten business categories are available")
            fixtures = [(f"sample-{i}.pdf", f"%PDF-1.4\nSYNTHETIC PRIVATE TEST {i}\n%%EOF".encode()) if i % 3 == 0 else (f"sample-{i}.xlsx", xlsx()) if i % 3 == 1 else (f"sample-{i}.docx", docx()) for i in range(20)]
            receipt = prepare(ca, a, fixtures)
            results = [ca.call(f"/api/evidence/uploads/{receipt['id']}/{i}", data) for i, (_, data) in enumerate(fixtures)]
            check(all(x["state"] in ("Uploaded", "Duplicate") for x in results), "One category accepts 20 mixed PDF/XLSX/DOCX files")
            w = workspace(ca, a)
            check(len(w["receipts"][0]["files"]) == 20 and all(v["validation"] == "Unreviewed" for v in w["versions"]), "Returning to workspace retains receipts; uploads are never auto-verified")
            retry_data=b'%PDF-1.4\nRETRY SYNTHETIC\n%%EOF'
            partial=prepare(ca,a,[('retry.pdf',retry_data),('invalid.pdf',b'wrong signature')])
            first=ca.call(f"/api/evidence/uploads/{partial['id']}/0",retry_data[:-3])
            check(first['state']=='Failed', 'Incomplete transfer has a persisted failed receipt')
            first=ca.call(f"/api/evidence/uploads/{partial['id']}/0",retry_data)
            second=ca.call(f"/api/evidence/uploads/{partial['id']}/1",b'wrong signature')
            check(first['state']=='Uploaded' and second['state']=='Rejected' and any(v['id']==first['versionId'] for v in workspace(ca,a)['versions']), 'Retrying only a failed file and a later rejected file do not lose the successful upload')
            source = results[0]
            duplicate = upload(ca, a, fixtures[0][0], fixtures[0][1], category="06")
            check(duplicate["state"] == "Duplicate" and duplicate["documentId"] == source["documentId"], "Same bytes tag another category without a second logical document")
            version = next(v for v in workspace(ca, a)["versions"] if v["id"] == source["versionId"])
            check(version["mime"] == "application/pdf" and version["hash"], "Detected MIME and immutable content hash retained")
            for path in [f"/api/evidence/versions/{version['id']}/import", f"/api/evidence/snapshots/{uuid.uuid4().hex}/publish"]:
                cb.call(path, {}, status=404)
            cb.call('/api/evidence/downloads', {"tenantId": a["id"], "versionIds": [version["id"]], "categoryId": "", "propertyId": "", "reportingPeriod": "", "archive": False}, status=404)
            check(True, "Guessed source IDs cannot cross organization authorization")
            dept_source = upload(department, a, "department-only.pdf", b"%PDF-1.4\nDEPARTMENT B SYNTHETIC\n%%EOF", scope="department-b")
            check(not any(v["id"] == dept_source["versionId"] for v in workspace(ca, a)["versions"]), "Shared organization does not imply cross-department source access")
            sensitive = upload(ca, a, "sensitive.csv", b"Tenant Name,Health Record\nSYNTHETIC,SYNTHETIC\n")
            check(sensitive["state"] == "Quarantined", "Sensitive test markers are quarantined")
            bad = upload(ca, a, "misnamed.pdf", b"not a PDF")
            check(bad["state"] == "Rejected", "File extension must match its signature")
            malware = upload(ca, a, "security.csv", b"EICAR-STANDARD-ANTIVIRUS-TEST-FILE")
            check(malware["state"] == "Quarantined", "Malicious test marker cannot enter normal review")
            ca.call('/api/evidence/downloads', {"tenantId": a["id"], "versionIds": [sensitive["versionId"]], "categoryId": "", "propertyId": "", "reportingPeriod": "", "archive": False}, status=400)
            check(True, "Quarantined source bytes are inaccessible even to the uploader")
            ca.call(f"/api/evidence/tenants/{a['id']}/facts.csv", status=404)
            ca.call('/api/submissions', status=403, raw=True)
            check(True, "Client cannot retrieve internal underwriting exports or legacy APIs")
            ca.call('/api/evidence/downloads', {"tenantId": a["id"], "versionIds": [version["id"]], "categoryId": "", "propertyId": "", "reportingPeriod": "", "archive": True}, status=404)
            check(True, "Clients cannot generate internal source archives")
            p1 = admin.call(f"/api/evidence/tenants/{a['id']}/properties", {"scopeId":"organization","clientPropertyId":"P1","name":"Property one","evidenceVersionId":version['id'],"locator":"page 1","reportingPeriod":"FY2026"})
            p2 = admin.call(f"/api/evidence/tenants/{a['id']}/properties", {"scopeId":"organization","clientPropertyId":"P2","name":"Property two","evidenceVersionId":version['id'],"locator":"page 1","reportingPeriod":"FY2026"})
            admin.call(f"/api/evidence/documents/{source['documentId']}", {"categoryIds":["02","06"],"propertyIds":[p1["id"],p2["id"]],"reportingPeriod":"FY2026","coverage":"Partial","notes":"Synthetic two-property relation"}, method="PUT")
            grants = admin.call('/api/evidence/downloads', {"tenantId":a["id"],"versionIds":[version["id"]],"categoryId":"06","propertyId":p2["id"],"reportingPeriod":"FY2026","archive":True})
            token = grants["parts"][0]["token"]
            archive = admin.call('/api/evidence/downloads/' + token, raw=True)
            with zipfile.ZipFile(io.BytesIO(archive)) as z:
                manifest = list(csv.DictReader(io.StringIO(z.read("manifest.csv").decode())))
                check(manifest[0]["version_id"] == version["id"] and p1["id"] in manifest[0]["property_ids"] and p2["id"] in manifest[0]["property_ids"], "One stored document is retrievable through either property with a precise archive manifest")
            admin.call('/api/evidence/downloads/' + token, status=404)
            check(True, "Download authorization is single-use")
            cb.call('/api/evidence/downloads/' + token, status=404)
            ca.call(f"/api/evidence/tenants/{a['id']}/categories/09/organization", {"state":"Not applicable","note":"Synthetic applicability"})
            ca.call(f"/api/evidence/tenants/{a['id']}/categories/09/organization", {"state":"Will provide later","note":"Now applicable"})
            check(True, "Availability can be revised independently of successful uploads")
            scoped_request = analyst.call(f"/api/evidence/tenants/{a['id']}/issues", {"scopeId":"organization","question":"Please provide the missing non-identifying agreement inventory.","severity":"Advisory","kind":"Missing evidence"})
            check(any(i['id']==scoped_request['id'] for i in workspace(ca,a)['issues']) and not any(i['id']==scoped_request['id'] for i in workspace(department,a)['issues']), "Source-free evidence requests respect department scope")
            ca.call(f"/api/evidence/issues/{scoped_request['id']}/response", {"response":"Expected next week","resolve":False,"availability":"Will provide later"})
            check(next(i for i in workspace(ca,a)['issues'] if i['id']==scoped_request['id'])['availability']=='Will provide later', "Availability is recorded independently for each requested evidence item")
            f1 = fact(admin, a, version["id"], 0)
            check(f1["value"] == 0, "Explicit zero remains distinct from unavailable evidence")
            unavailable=admin.call(f"/api/evidence/tenants/{a['id']}/facts", {"documentVersionId":version['id'],"metricCode":"RESTRICTED_CASH","value":None,"unit":"Currency","currency":"CAD","period":"FY2026","basis":"Management YTD","locator":"page 1","certainty":"Unavailable","reason":"The source does not disclose this balance"})
            admin.call(f"/api/evidence/facts/{unavailable['id']}/review", {"status":"Verified","reason":"Cannot invent a number"},status=400)
            check(unavailable['value'] is None,'Unavailable evidence remains null and cannot be verified as zero')
            f2 = fact(admin, a, version["id"], 100)
            issues = workspace(admin, a)["issues"]
            conflict = next(i for i in issues if i["factId"] == f2["id"])
            admin.call(f"/api/evidence/facts/{f2['id']}/review", {"status":"Verified","reason":"test"}, status=400)
            check(True, "Contradictory balances create an exception and block verification")
            admin.call(f"/api/evidence/facts/{f2['id']}/review", {"status":"Rejected","reason":"Synthetic conflicting candidate rejected"})
            admin.call(f"/api/evidence/issues/{conflict['id']}/response", {"response":"Selected source-supported zero; competing figure rejected","resolve":True})
            admin.call(f"/api/evidence/facts/{f1['id']}/review", {"status":"Verified","reason":"Source and reporting basis checked"})
            check(not workspace(ca, a)["facts"], "Client cannot see internal workbench values before a validation summary")
            csv_input = b"metric_code,value,unit,currency,period,as_of_date,basis\nREVENUE,200,Currency,CAD,FY2027,2027-03-31,Audited fiscal year\n"
            structured = upload(ca, a, "structured.csv", csv_input)
            imported = admin.call(f"/api/evidence/versions/{structured['versionId']}/import", {})
            check(imported["inserted"] == 1, "Structured CSV import creates unreviewed source-linked fact")
            imported_fact = next(f for f in workspace(admin, a)["facts"] if f["documentVersionId"] == structured["versionId"])
            check(imported_fact["locator"] == "CSV!B2" and imported_fact["unit"] == "Currency" and imported_fact["status"] == "Unreviewed", "Imported field preserves exact cell, unit and review state")
            policy(admin, a)
            snap = admin.call(f"/api/evidence/tenants/{a['id']}/snapshots", {"cutoff":"2026-10-31","modelVersion":"TEST-MODEL/1","reportVersion":"1","assumptions":"INTERNAL PRIVATE ASSUMPTIONS","reportText":"Approved aggregate client report","scopeDescription":"Synthetic FY2026 sample baseline","facts":[{"id":f1["id"]}]})
            snapshot_bytes = Path(folder, "snapshots", snap["id"] + ".enc").read_bytes()
            client_snap = workspace(ca, a)["snapshots"][0]
            check("assumptions" not in client_snap and client_snap["report"] is None, "Client validation summary excludes model assumptions and draft report")
            ca.call('/api/evidence/snapshots/' + snap["id"], status=404)
            admin.call(f"/api/evidence/snapshots/{snap['id']}/publish", {}, status=400)
            ca.call('/api/evidence/snapshots/validate', {"snapshotId":snap["id"],"decision":"Facts confirmed","note":"test","authorizePublication":True}, status=404)
            approver.call('/api/evidence/snapshots/validate', {"snapshotId":snap["id"],"decision":"Facts confirmed","note":"Factual baseline confirmed for sample only","authorizePublication":True})
            admin.call(f"/api/evidence/snapshots/{snap['id']}/publish", {})
            check(workspace(ca, a)["snapshots"][0]["report"] == "Approved aggregate client report", "Client factual sign-off and separate administrator publication are enforced")
            approver.call('/api/evidence/snapshots/validate', {"snapshotId":snap["id"],"decision":"Facts confirmed","note":"Withdraw publication permission","authorizePublication":False})
            check(workspace(ca,a)['snapshots'][0]['report'] is None,'Client withdrawal removes report visibility while preserving frozen data')
            approver.call('/api/evidence/snapshots/validate', {"snapshotId":snap["id"],"decision":"Facts confirmed","note":"Reauthorize publication for this test","authorizePublication":True})
            admin.call(f"/api/evidence/snapshots/{snap['id']}/publish", {})
            correction = fact(admin, a, version["id"], 1, previous=f1["id"])
            correction_issues = [i for i in workspace(admin, a)["issues"] if i["factId"] == correction["id"] and i["status"] == "Open"]
            for issue in correction_issues:
                admin.call(f"/api/evidence/issues/{issue['id']}/response", {"response":"Explicit correction selected","resolve":True})
            admin.call(f"/api/evidence/facts/{correction['id']}/review", {"status":"Verified","reason":"New corrected factual record"})
            check(Path(folder,"snapshots",snap["id"]+".enc").read_bytes() == snapshot_bytes, "Frozen snapshot bytes remain unchanged after a later correction/upload")
            old = admin.call('/api/evidence/snapshots/' + snap["id"])
            check(old["facts"][0]["value"] == 0, "Original factual baseline survives correction")
            replacement = upload(ca, a, "revised.pdf", b"%PDF-1.4\nREVISED SYNTHETIC SOURCE\n%%EOF", existing=source["documentId"])
            old_v = next(v for v in workspace(ca, a)["versions"] if v["id"] == version["id"])
            new_v = next(v for v in workspace(ca, a)["versions"] if v["id"] == replacement["versionId"])
            check(new_v["supersedesId"] == old_v["id"] and new_v["number"] == 2, "Revised schedule preserves distinguishable prior versions")
            admin.call(f"/api/evidence/versions/{version['id']}/delete", {"reason":"Authorized synthetic disposal","authorized":True,"backupAndRetrievalReviewed":True}, status=400)
            check(True, 'Source deletion is blocked until the latest verified correction is frozen')
            admin.call(f"/api/evidence/tenants/{a['id']}/snapshots", {"cutoff":"2026-10-31","modelVersion":"TEST-MODEL/1","reportVersion":"2","scopeDescription":"Corrected synthetic baseline","facts":[{"id":correction["id"]}]})
            admin.call(f"/api/evidence/versions/{version['id']}/delete", {"reason":"Authorized synthetic disposal","authorized":True,"backupAndRetrievalReviewed":True})
            check(Path(folder,"snapshots",snap["id"]+".enc").read_bytes() == snapshot_bytes, "Authorized temporary-source deletion does not delete analytical snapshot")
            availability = workspace(admin,a)["sourceAvailability"]
            check(not next(v for v in availability if v["id"] == version["id"])["available"], "Deleted source reference is visibly unavailable")
            audit = admin.call(f"/api/evidence/tenants/{a['id']}/audit")
            check(any(e["action"] == "Temporary source deletion authorized" for e in audit) and sum(e["action"] == "Category completeness changed" for e in audit) == 2, "Deletion and category revisions are audited")
            oversized = {"tenantId":a["id"],"scopeId":"organization","categoryIds":["02"],"coverage":"Partial","confirmedNoPersonalData":True,"files":[{"name":"large.pdf","size":1048577}]}
            ca.call('/api/evidence/uploads/prepare', oversized, status=400)
            check(True, "Oversized transfer is refused before bytes are sent")
            reserved_files=[(f'reserved-{i}.csv',b'x'*1048576) for i in range(4)]
            quota_reservation=prepare(cb,b,reserved_files)
            cb.call('/api/evidence/uploads/prepare', {"tenantId":b['id'],"scopeId":"organization","categoryIds":["02"],"coverage":"Partial","confirmedNoPersonalData":True,"files":[{"name":n,"size":len(v)} for n,v in reserved_files]}, status=400)
            cb.call(f"/api/evidence/uploads/{quota_reservation['id']}/cancel",{})
            check(True, "Concurrent reservations enforce tenant quota before transfer; cancellation releases capacity")
            reference = ca.call(f"/api/evidence/tenants/{a['id']}/references", {"scope":"organization","category":"09","reference":"https://client.example.test/preserved-source","label":"Client-held agreement"})
            ca.call(f"/api/evidence/versions/{reference['version']['id']}/unavailable", {"state":"Unavailable","note":"Client revoked sharing"})
            check(next(v for v in workspace(ca,a)["versions"] if v["id"] == reference["version"]["id"])["processing"] == "Access revoked", "Withdrawn client-hosted access is explicit without erasing history")
            check(all(b"SYNTHETIC PRIVATE TEST" not in p.read_bytes() and b"INTERNAL PRIVATE ASSUMPTIONS" not in p.read_bytes() for p in Path(folder).rglob('*.enc')), "Originals, metadata and snapshots are encrypted at rest")
            # Internal test access is revoked immediately, including the current encrypted session.
            users = admin.call(f"/api/evidence/tenants/{a['id']}/users")
            user_id = next(u["id"] for u in users if u["email"] == "a@example.test")
            admin.call(f"/api/evidence/tenants/{a['id']}/users/{user_id}", method="DELETE")
            check(not ca.session()["authenticated"], "Revocation invalidates an already issued client session")
            if os.environ.get('EVIDENCE_BROWSER_TEST'):
                browser_env=dict(os.environ,EVIDENCE_TEST_URL=BASE,EVIDENCE_TEST_PASSWORD=PASSWORD,EVIDENCE_TEST_TOTP=totp(),EVIDENCE_TEST_TOTP_SECRET=TOTP_SECRET)
                subprocess.run([os.environ.get('NODE_EXE','node'),'tests/browser_evidence.cjs'],cwd=ROOT,env=browser_env,check=True)
            print(f"EVIDENCE PORTAL: {passed} synthetic integration checks passed.", flush=True)
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
            log.close()


if __name__ == "__main__":
    main()

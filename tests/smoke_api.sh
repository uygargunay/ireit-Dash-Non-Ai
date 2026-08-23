#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
smoke_root="$(mktemp -d)"
server_pid=""
port="5079"

cleanup() {
  if [[ -n "$server_pid" ]]; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
  if [[ -d "$smoke_root" && "$smoke_root" == /tmp/* ]]; then
    rm -r -- "$smoke_root"
  fi
}
trap cleanup EXIT

export Irei__DataRoot="$smoke_root/data"
export Irei__AdminKey="smoke-test-key"

dotnet run \
  --no-build \
  --configuration Release \
  --project "$repo_root/src/IreiMvp.Web/IreiMvp.Web.csproj" \
  --no-launch-profile \
  --urls "http://127.0.0.1:$port" \
  >"$smoke_root/server.log" 2>&1 &
server_pid="$!"

for _ in {1..60}; do
  if curl --silent --fail "http://127.0.0.1:$port/health" >"$smoke_root/health.json"; then
    break
  fi
  if ! kill -0 "$server_pid" 2>/dev/null; then
    cat "$smoke_root/server.log"
    exit 1
  fi
  sleep 1
done
curl --silent --fail "http://127.0.0.1:$port/health" >/dev/null

curl --silent --show-error --fail \
  -F "organizationName=CI Housing Society" \
  -F "file=@$repo_root/src/IreiMvp.Web/Data/Examples/IREI_MVP_Stage1_V2_NGOB_Demo.xlsx" \
  "http://127.0.0.1:$port/api/submissions" \
  >"$smoke_root/submission.json"

submission_id="$(python - "$smoke_root/submission.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
assert data['versionStatus'] == 'Working'
assert data['dashboard']['totalProperties'] > 0
assert len(data['dashboard']['properties']) == data['dashboard']['totalProperties']
assert data['dashboard']['reportingPeriod']
assert data['sourceDocuments']
print(data['id'])
PY
)"

curl --silent --show-error --fail \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"CI Reviewer"}' \
  "http://127.0.0.1:$port/api/submissions/$submission_id/approve" \
  >"$smoke_root/approved.json"

python - "$smoke_root/approved.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
assert data['versionStatus'] == 'Approved'
assert data['lastVerifiedUtc']
assert all(item['verificationStatus'] == 'Verified' for item in data['evidence'])
PY

curl --silent --show-error --fail \
  -H "Content-Type: application/json" \
  -d '{"issue":"CI persistent action","entityId":null,"cause":null,"impact":null,"response":null,"owner":"CFO","dueDate":"2026-09-30T00:00:00.000Z","decisionBody":"Management","riskLevel":"Medium","evidenceReference":null}' \
  "http://127.0.0.1:$port/api/submissions/$submission_id/actions" \
  >"$smoke_root/action.json"

curl --silent --show-error --fail \
  -H "Content-Type: application/json" \
  -d '{"obligation":"CI annual filing","source":null,"owner":null,"dueDate":"2027-06-30T00:00:00.000Z","recurrence":null,"notes":null}' \
  "http://127.0.0.1:$port/api/submissions/$submission_id/obligations" \
  >"$smoke_root/obligation.json"

curl --silent --show-error --fail \
  -H "Content-Type: application/json" \
  -d '{"reportName":"CI Board Report","purpose":"Automated verification","recipient":"Board","scope":"Approved assessment"}' \
  "http://127.0.0.1:$port/api/submissions/$submission_id/reports" \
  >"$smoke_root/report.json"

report_id="$(python - "$smoke_root/report.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
assert data['status'] == 'Draft'
assert data['artifactAvailable'] is False
print(data['reportId'])
PY
)"

curl --silent --show-error --fail \
  -H "Content-Type: application/json" \
  -d '{"approvedBy":"CI Board Chair"}' \
  "http://127.0.0.1:$port/api/submissions/$submission_id/reports/$report_id/approve" \
  >"$smoke_root/report-approved.json"

curl --silent --show-error --fail \
  -H "X-Admin-Key: smoke-test-key" \
  "http://127.0.0.1:$port/api/submissions/$submission_id/reports/$report_id/download" \
  >"$smoke_root/report.xlsx"

python - "$smoke_root/report.xlsx" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as workbook:
    assert 'xl/workbook.xml' in workbook.namelist()
PY

curl --silent --show-error --fail \
  "http://127.0.0.1:$port/api/submissions/$submission_id" \
  >"$smoke_root/final.json"

python - "$smoke_root/final.json" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
assert any(item['issue'] == 'CI persistent action' for item in data['actions'])
assert any(item['obligation'] == 'CI annual filing' for item in data['obligations'])
assert any(item['reportName'] == 'CI Board Report' and item['status'] == 'Approved' for item in data['reports'])
for source in data['sourceDocuments']:
    assert 'storedPath' not in source
for report in data['reports']:
    assert 'artifactPath' not in report
PY

first_version_id="$(python - "$smoke_root/final.json" <<'PY'
import json, sys
print(json.load(open(sys.argv[1], encoding='utf-8'))['versionId'])
PY
)"

curl --silent --show-error --fail \
  -F "organizationName=CI Housing Society" \
  -F "file=@$repo_root/src/IreiMvp.Web/Data/Examples/IREI_MVP_Stage1_V2_NGOB_Demo.xlsx" \
  "http://127.0.0.1:$port/api/submissions" \
  >"$smoke_root/update.json"

python - "$smoke_root/update.json" "$first_version_id" <<'PY'
import json, sys
data=json.load(open(sys.argv[1], encoding='utf-8'))
assert data['versionStatus'] == 'Working'
assert data['parentVersionId'] == sys.argv[2]
assert data['versionId'] != sys.argv[2]
assert any(item['issue'] == 'CI persistent action' for item in data['actions'])
assert not any(item['isMaterial'] for item in data['changes'])
assert len(data['versions']) == 2
PY

echo "IREI Stage 1 V2 API smoke test passed."

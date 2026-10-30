#!/usr/bin/env bash
# End-to-end smoke test of the seven demo scenarios against a running API.
# Usage: ./scripts/smoke.sh [base_url]      (default http://localhost:5080)
# Requires: curl, python3 (only for reading JSON fields).
set -uo pipefail

BASE="${1:-http://localhost:5080}"
RUN="$(date +%s)"
FAILED=0

alice() { curl -s -H "X-Demo-User: alice@example.local" -H 'Content-Type: application/json' "$@"; }
bob()   { curl -s -H "X-Demo-User: bob@example.local"   -H 'Content-Type: application/json' "$@"; }
carol() { curl -s -H "X-Demo-User: carol@example.local" -H 'Content-Type: application/json' "$@"; }
dana()  { curl -s -H "X-Demo-User: dana@example.local"  -H 'Content-Type: application/json' "$@"; }
erin()  { curl -s -H "X-Demo-User: erin@example.local"  -H 'Content-Type: application/json' "$@"; }

field() { python -c "import sys,json;d=json.load(sys.stdin);print(d$1)" 2>/dev/null; }
code()  { python -c "import sys,json;d=json.load(sys.stdin);print(d.get('code',''))" 2>/dev/null; }

check() { # check <label> <expected> <actual>
  if [ "$2" = "$3" ]; then
    printf '  ok   %-46s %s\n' "$1" "$3"
  else
    printf '  FAIL %-46s expected %s, got %s\n' "$1" "$2" "$3"
    FAILED=$((FAILED + 1))
  fi
}

create() { # create <client> <clientRequestId> <appId> <env> <level>
  $1 -X POST "$BASE/api/requests" -d "{\"clientRequestId\":\"$2\",\"applicationId\":$3,\"environment\":\"$4\",\"accessLevel\":\"$5\",\"justification\":\"Smoke test run $RUN: verifying the phase 1 workflow end to end\"}"
}

echo "== 1 standard request: manager approval is enough"
R=$(create alice "smoke-$RUN-1" 1 NonProduction Read)
ID=$(echo "$R" | field "['id']"); VER=$(echo "$R" | field "['version']")
check "created status" "PendingManagerApproval" "$(echo "$R" | field "['status']")"
check "bob approve ->" "Approved" "$(bob -X POST "$BASE/api/requests/$ID/approve" -d "{\"version\":$VER}" | field "['status']")"

echo "== 2 production request: second approval by system owner"
R=$(create alice "smoke-$RUN-2" 1 Production Read)
ID=$(echo "$R" | field "['id']")
check "high risk" "True" "$(echo "$R" | field "['isHighRisk']")"
A=$(bob -X POST "$BASE/api/requests/$ID/approve" -d '{"version":1}')
check "after manager" "PendingSystemOwnerApproval" "$(echo "$A" | field "['status']")"
check "carol approve ->" "Approved" "$(carol -X POST "$BASE/api/requests/$ID/approve" -d '{"version":2}' | field "['status']")"

echo "== 3 admin access: owner of that application must approve"
R=$(create alice "smoke-$RUN-3" 2 NonProduction Admin)
ID=$(echo "$R" | field "['id']")
A=$(bob -X POST "$BASE/api/requests/$ID/approve" -d '{"version":1}')
check "after manager" "PendingSystemOwnerApproval" "$(echo "$A" | field "['status']")"
check "dana approve ->" "Approved" "$(dana -X POST "$BASE/api/requests/$ID/approve" -d '{"version":2}' | field "['status']")"

echo "== 4 unauthorized approval: admin is not an approver"
R=$(create alice "smoke-$RUN-4" 1 Production Read)
ID=$(echo "$R" | field "['id']")
check "http status" "403" "$(erin -o /dev/null -w '%{http_code}' -X POST "$BASE/api/requests/$ID/approve" -d '{"version":1}')"
check "error code" "not_assigned_approver" "$(erin -X POST "$BASE/api/requests/$ID/approve" -d '{"version":1}' | code)"

echo "== 5 duplicate submit: same clientRequestId collapses into one request"
FIRST=$(create alice "smoke-$RUN-5" 1 NonProduction Read)
SECOND=$(create alice "smoke-$RUN-5" 1 NonProduction Read)
check "same request id" "$(echo "$FIRST" | field "['id']")" "$(echo "$SECOND" | field "['id']")"

echo "== 6 stale version: second action on the same version conflicts"
R=$(create alice "smoke-$RUN-6" 1 Production Read)
ID=$(echo "$R" | field "['id']")
check "http status" "409" "$(bob -o /dev/null -w '%{http_code}' -X POST "$BASE/api/requests/$ID/approve" -d '{"version":99}')"
check "error code" "version_conflict" "$(bob -X POST "$BASE/api/requests/$ID/approve" -d '{"version":99}' | code)"

echo "== 7 rejection: reason required, then terminal"
R=$(create alice "smoke-$RUN-7" 1 NonProduction Read)
ID=$(echo "$R" | field "['id']")
check "no reason rejected" "400" "$(bob -o /dev/null -w '%{http_code}' -X POST "$BASE/api/requests/$ID/reject" -d '{"version":1}')"
check "with reason" "Rejected" "$(bob -X POST "$BASE/api/requests/$ID/reject" -d '{"version":1,"reason":"Use the shared reporting account instead"}' | field "['status']")"
check "terminal" "409" "$(bob -o /dev/null -w '%{http_code}' -X POST "$BASE/api/requests/$ID/approve" -d '{"version":2}')"
check "audit entries" "2" "$(alice "$BASE/api/requests/$ID" | python -c 'import sys,json;print(len(json.load(sys.stdin)["audit"]))')"

echo
if [ "$FAILED" -eq 0 ]; then
  echo "ALL SMOKE CHECKS PASSED"
else
  echo "$FAILED SMOKE CHECK(S) FAILED"
  exit 1
fi

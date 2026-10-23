#!/usr/bin/env bash
# End-to-end check of every endpoint. Usage: API_KEY=... ./tools/smoke_test.sh [base_url]
set -u
BASE="${1:-http://127.0.0.1:8080}"
: "${API_KEY:?export API_KEY first (same value as in .env)}"
pass=0; fail=0
check() {  # name expected_code curl-args...
  local name="$1" want="$2"; shift 2
  local code; code=$(curl -s -o /tmp/smoke_body -w '%{http_code}' "$@")
  if [[ "$code" == "$want" ]]; then echo "PASS $name ($code)"; pass=$((pass+1))
  else echo "FAIL $name: got $code want $want"; head -c 300 /tmp/smoke_body; echo; fail=$((fail+1)); fi
}
H=(-H "X-Api-Key: $API_KEY")
J=(-H "Content-Type: application/json")
check "health (no key)"            200 "$BASE/health"
check "sectors without key"        401 "$BASE/api/sectors"
check "sectors with wrong key"     401 -H "X-Api-Key: wrong" "$BASE/api/sectors"
check "sectors"                    200 "${H[@]}" "$BASE/api/sectors"
check "companies"                  200 "${H[@]}" "$BASE/api/companies"
check "companies by sector"        200 "${H[@]}" "$BASE/api/companies?sector=Energy"
check "company CSL"                200 "${H[@]}" "$BASE/api/companies/CSL"
check "company bad ticker"         400 "${H[@]}" "$BASE/api/companies/CSL%27--"
check "company unknown"            404 "${H[@]}" "$BASE/api/companies/ZZZZ"
check "CSL returns 2019"           200 "${H[@]}" "$BASE/api/companies/CSL/returns?from=2019-01-01&to=2019-12-31"
check "sector tickers"             200 "${H[@]}" "$BASE/api/sectors/Health%20Care/tickers"
check "sector stats"               200 "${H[@]}" "$BASE/api/sectors/Health%20Care/stats?from=2010-01-01"
check "sector unknown"             404 "${H[@]}" "$BASE/api/sectors/Nope/stats"
check "optimise sector"            200 "${H[@]}" "${J[@]}" -X POST "$BASE/api/portfolios/optimise" \
  -d '{"sector":"Health Care","from":"2012-01-01","to":"2019-12-31","method":"risk_parity","maxWeight":0.35}'
check "optimise tickers"           200 "${H[@]}" "${J[@]}" -X POST "$BASE/api/portfolios/optimise" \
  -d '{"tickers":["CBA","BHP","CSL","WOW","TLS"],"from":"2012-01-01","to":"2019-12-31","method":"min_variance"}'
check "optimise maxWeight too low" 400 "${H[@]}" "${J[@]}" -X POST "$BASE/api/portfolios/optimise" \
  -d '{"sector":"Health Care","from":"2012-01-01","to":"2019-12-31","maxWeight":0.05}'
check "optimise bad method"        400 "${H[@]}" "${J[@]}" -X POST "$BASE/api/portfolios/optimise" \
  -d '{"sector":"Health Care","from":"2012-01-01","to":"2019-12-31","method":"yolo"}'
echo "---- $pass passed, $fail failed"
[[ $fail -eq 0 ]]

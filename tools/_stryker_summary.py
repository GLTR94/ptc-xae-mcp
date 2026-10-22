import sys, json
from collections import Counter

html = open(sys.argv[1], encoding='utf-8').read()
i = html.find('app.report = ')
start = html.find('{', i)
BS = chr(92)
depth = 0; instr = False; esc = False; end = None
for j in range(start, len(html)):
    ch = html[j]
    if esc:
        esc = False; continue
    if ch == BS:
        esc = True; continue
    if ch == '"':
        instr = not instr; continue
    if instr:
        continue
    if ch == '{':
        depth += 1
    elif ch == '}':
        depth -= 1
        if depth == 0:
            end = j + 1; break

data = json.loads(html[start:end])
files = data.get("files", {})
status_total = Counter(); per_file = {}
survivors = []
for path, fobj in files.items():
    short = path.split('ptc-xae-mcp' + BS + BS)[-1].replace(BS + BS, '/')
    c = Counter()
    for mu in fobj.get("mutants", []):
        c[mu["status"]] += 1
        if mu["status"] in ("Survived", "NoCoverage"):
            loc = mu.get("location", {}).get("start", {})
            survivors.append((short, mu["status"], mu.get("mutatorName"), loc.get("line"), mu.get("replacement", "")[:60]))
    per_file[path.split('ptc-xae-mcp' + BS + BS)[-1].replace(BS + BS, '/')] = c
    status_total.update(c)

killed = status_total.get("Killed", 0); surv = status_total.get("Survived", 0)
noc = status_total.get("NoCoverage", 0); to = status_total.get("Timeout", 0)
detected = killed + to; valid = detected + surv + noc
print("OVERALL:", dict(status_total))
print(f"Mutation score = {100*detected/valid:.1f}%  killed={killed} timeout={to} survived={surv} nocoverage={noc} (valid={valid})")

print("\n=== Files with surviving/uncovered mutants ===")
rows = []
for p, c in per_file.items():
    bad = c.get("Survived", 0) + c.get("NoCoverage", 0)
    if bad > 0:
        rows.append((bad, c.get("Survived", 0), c.get("NoCoverage", 0), sum(c.values()), p))
for bad, s, n, tot, p in sorted(rows, reverse=True):
    print(f"  bad={bad:3d} surv={s:3d} nocov={n:3d} /{tot:3d}  {p}")

if len(sys.argv) > 2 and sys.argv[2] == "detail":
    print("\n=== Survivor / NoCoverage detail ===")
    for short, st, mut, line, repl in sorted(survivors):
        print(f"  {short}:{line}  [{st}] {mut} -> {repl!r}")

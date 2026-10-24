const {DISC, SEALS, ITEMS, NODES} = require("./data.js");
const by = Object.fromEntries(NODES.map(n => [n.c, n]));
let err = 0; const E = m => { console.log("ERR", m); err++; };
const prod = {}; NODES.forEach(n => (n.mk || []).forEach(i => (prod[i] = prod[i] || []).push(n.c)));
for (const n of NODES) {
  for (const r of [...n.r, ...(n.any || [])]) { if (!by[r]) E(`${n.c} unknown req ${r}`); else if (by[r].t > n.t) E(`${n.c} t${n.t} needs ${r} t${by[r].t}`); }
  for (const [i] of [...(n.$ || []), ...(n.bp || [])]) if (!ITEMS[i]) E(`${n.c} unknown item ${i}`);
  if (n.d && !DISC.find(d => d.k === n.d).paths[n.p]) E(`${n.c} bad path ${n.p}`);
}
// ancestor closure (any: try each alternative)
const memo = {};
function anc(c, seen = new Set()) {
  if (memo[c]) return memo[c]; if (seen.has(c)) { E("cycle at " + c); return new Set(); } seen.add(c);
  const n = by[c], s = new Set([c]);
  n.r.forEach(r => anc(r, seen).forEach(x => s.add(x)));
  seen.delete(c); return (memo[c] = s);
}
for (const n of NODES) {
  const alts = n.any ? n.any.map(a => new Set([...anc(n.c), ...anc(a)])) : [anc(n.c)];
  for (const [i] of [...(n.$ || []), ...(n.bp || [])]) {
    const p = prod[i]; if (!p) continue;
    if (!alts.some(s => p.some(x => s.has(x)))) E(`${n.c} uses ${i} (made by ${p}) not in its requirements`);
  }
}
// reachability with seals
const done = new Set(["R0"]);
const cnt = () => { const m = {}; done.forEach(c => { const d = by[c].d; if (d) m[d] = (m[d] || 0) + 1; }); return m; };
function sealOk(t) { if (t < 2) return true; const s = SEALS[t], m = cnt(); return done.size - 1 >= s.total && Object.values(m).filter(v => v >= s.each).length >= s.disc; }
let step = 0, prog = true;
while (prog) { prog = false; step++;
  for (const n of NODES) if (!done.has(n.c) && n.r.every(r => done.has(r)) && (!n.any || n.any.some(a => done.has(a))) && sealOk(n.t)) { done.add(n.c); prog = true; }
}
NODES.filter(n => !done.has(n.c)).forEach(n => E("unreachable " + n.c));
// stats
const st = {}; NODES.forEach(n => { const k = n.d || "root"; st[k] = st[k] || {R:0,D:0,B:0,K:0,all:0}; st[k][n.k]++; st[k].all++; });
console.log(st, "total", NODES.length, "cross", NODES.reduce((a, n) => a + n.r.filter(r => by[r].d && by[r].d !== n.d).length + (n.any || []).length, 0));
console.log("new items", Object.values(ITEMS).filter(i => i[2] === "N").length, "unused items", Object.keys(ITEMS).filter(i => !NODES.some(n => [...(n.$||[]),...(n.bp||[])].some(([x]) => x === i))));
console.log(err ? err + " errors" : "OK");

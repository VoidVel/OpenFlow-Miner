// Reference client for the OpenFlow Miner API. It uses ONLY the public HTTP API — no privileged
// backchannel. If this file were deleted, the product would be entirely intact (the API is the product).

const API = "/api/v1/event-logs";
const $ = (id) => document.getElementById(id);

const statusEl = $("status");
function setStatus(message, kind = "") {
  statusEl.textContent = message;
  statusEl.className = "status " + kind;
}

// --- Loading a log (demo or freshly uploaded) --------------------------------------------

async function showLog(id) {
  setStatus("Mining " + id + " …");
  try {
    const [summary, dfg, variants, bottlenecks] = await Promise.all([
      getJson(`${API}/${id}`),
      getJson(`${API}/${id}/dfg`),
      getJson(`${API}/${id}/variants?top=5`),
      getJson(`${API}/${id}/bottlenecks?top=5`),
    ]);

    renderSummary(summary);
    renderVariants(variants);
    renderBottlenecks(bottlenecks);
    $("mermaid-link").href = `${API}/${id}/dfg?format=mermaid`;

    // Reveal the panels BEFORE rendering the graph, so the container has real dimensions when
    // Cytoscape lays out (a hidden display:none container has zero size and would cram the graph).
    $("summary-card").hidden = false;
    $("results").hidden = false;
    renderGraph(dfg);

    setStatus(`Mined ${summary.caseCount.toLocaleString()} cases · ${summary.eventCount.toLocaleString()} events`, "ok");
  } catch (err) {
    setStatus(err.message, "error");
  }
}

async function getJson(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(await describeError(res));
  return res.json();
}

async function describeError(res) {
  try {
    const problem = await res.json();
    if (problem.errors) {
      return `${problem.title}: ` + problem.errors.map((e) => `row ${e.row} ${e.code}`).join(", ");
    }
    return problem.detail || problem.title || `HTTP ${res.status}`;
  } catch {
    return `HTTP ${res.status}`;
  }
}

// --- Rendering ---------------------------------------------------------------------------

function renderSummary(s) {
  $("summary").innerHTML = `
    <dt>Log</dt><dd>${escapeHtml(s.provenance.source)}</dd>
    <dt>Cases</dt><dd>${s.caseCount.toLocaleString()}</dd>
    <dt>Events</dt><dd>${s.eventCount.toLocaleString()}</dd>
    <dt>Activities</dt><dd>${s.activityCount}</dd>
    <dt>Variants</dt><dd>${s.variantCount}</dd>
    ${s.provenance.attribution ? `<dt>Source</dt><dd>${escapeHtml(s.provenance.attribution)}</dd>` : ""}
  `;
}

let cy = null;
let lastDfg = null;

// Pull the (theme-aware) graph palette from CSS custom properties so the graph matches dark/light.
function graphPalette() {
  const s = getComputedStyle(document.documentElement);
  const v = (name) => s.getPropertyValue(name).trim();
  return {
    nodeBg: v("--graph-node-bg"), nodeBorder: v("--graph-node-border"), nodeInk: v("--graph-node-ink"),
    startBg: v("--graph-start-bg"), startBorder: v("--graph-start-border"), startInk: v("--graph-start-ink"),
    endBg: v("--graph-end-bg"), endBorder: v("--graph-end-border"), endInk: v("--graph-end-ink"),
    edge: v("--graph-edge"), labelInk: v("--graph-label-ink"), labelBg: v("--graph-bg"),
  };
}

function renderGraph(dfg) {
  lastDfg = dfg;
  const p = graphPalette();
  const starts = new Set(dfg.startActivities);
  const ends = new Set(dfg.endActivities);
  const maxFreq = Math.max(1, ...dfg.edges.map((e) => e.frequency));

  const elements = [];
  for (const n of dfg.nodes) {
    let role = "step";
    if (starts.has(n.activity)) role = "start";
    else if (ends.has(n.activity)) role = "end";
    elements.push({ data: { id: n.activity, label: `${n.activity}\n(${n.frequency})`, role } });
  }
  for (const e of dfg.edges) {
    elements.push({
      data: { source: e.from, target: e.to, label: String(e.frequency), width: 1 + (6 * e.frequency) / maxFreq },
    });
  }

  if (cy) cy.destroy();
  cy = cytoscape({
    container: $("graph"),
    elements,
    style: [
      {
        selector: "node",
        style: {
          "label": "data(label)", "text-wrap": "wrap", "text-valign": "center", "text-halign": "center",
          "text-max-width": "120px", "font-size": "11px", "background-color": p.nodeBg,
          "border-width": 1, "border-color": p.nodeBorder, "shape": "round-rectangle",
          "width": "label", "height": "label", "padding": "10px", "color": p.nodeInk,
        },
      },
      { selector: 'node[role="start"]', style: { "background-color": p.startBg, "border-color": p.startBorder, "color": p.startInk } },
      { selector: 'node[role="end"]', style: { "background-color": p.endBg, "border-color": p.endBorder, "color": p.endInk } },
      {
        selector: "edge",
        style: {
          "width": "data(width)", "label": "data(label)", "font-size": "10px", "color": p.labelInk,
          "line-color": p.edge, "target-arrow-color": p.edge, "target-arrow-shape": "triangle",
          "curve-style": "bezier", "text-background-color": p.labelBg, "text-background-opacity": 1,
          "text-background-padding": "2px",
        },
      },
    ],
    layout: { name: "breadthfirst", directed: true, spacingFactor: 1.1, padding: 24, fit: true },
  });

  // Belt-and-suspenders: re-fit once the container is definitely sized and layout has settled.
  cy.one("layoutstop", () => {
    cy.resize();
    cy.fit(undefined, 24);
  });
}

function renderVariants(variants) {
  $("variants").innerHTML = variants
    .map(
      (v) => `<li><span class="pct">${v.percentage}%</span> (${v.caseCount})<br>
        <span class="seq">${v.sequence.map(escapeHtml).join('<span class="arrow"> → </span>')}</span></li>`
    )
    .join("");
}

function renderBottlenecks(bottlenecks) {
  $("bottlenecks").innerHTML = bottlenecks
    .map(
      (b) => `<li><strong>${escapeHtml(b.activity)}</strong><br>
        avg wait <span class="wait">${b.averageTimeToNext}</span> · ${b.observations} obs</li>`
    )
    .join("");
}

function escapeHtml(s) {
  return s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

// --- Wiring ------------------------------------------------------------------------------

$("load-demo").addEventListener("click", () => showLog($("demo-select").value));

$("upload-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const file = $("file").files[0];
  if (!file) return;

  const form = new FormData();
  form.append("file", file);
  for (const key of ["caseId", "activity", "timestamp", "resource"]) {
    const value = $(key).value.trim();
    if (value) form.append(key, value);
  }

  setStatus("Uploading & mining …");
  try {
    const res = await fetch(API, { method: "POST", body: form });
    if (!res.ok) throw new Error(await describeError(res));
    const summary = await res.json();
    await showLog(summary.id);
  } catch (err) {
    setStatus(err.message, "error");
  }
});

// Re-render the graph with the new palette when the theme is toggled.
document.addEventListener("themechange", () => {
  if (lastDfg) renderGraph(lastDfg);
});

// Show the real dataset immediately on first load (Success 10.2).
showLog("receipt");

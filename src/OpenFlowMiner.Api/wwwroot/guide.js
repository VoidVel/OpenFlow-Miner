// Interactive endpoint guide. Every "Try it" button calls the real, running API over HTTP — using
// only the public endpoints, exactly as an external developer would.

const $ = (id) => document.getElementById(id);
const logId = () => ($("log-id").value.trim() || "receipt");
const apiKey = () => $("api-key").value.trim();

// Each action returns { method, url, body?, headers? }.
const actions = {
  summary: () => ({ method: "GET", url: `/api/v1/event-logs/${enc(logId())}` }),
  dfg: () => ({ method: "GET", url: `/api/v1/event-logs/${enc(logId())}/dfg?format=${$("dfg-format").value}` }),
  variants: () => ({ method: "GET", url: `/api/v1/event-logs/${enc(logId())}/variants?top=${clampTop("variants-top")}` }),
  statistics: () => ({ method: "GET", url: `/api/v1/event-logs/${enc(logId())}/statistics` }),
  bottlenecks: () => ({ method: "GET", url: `/api/v1/event-logs/${enc(logId())}/bottlenecks?top=${clampTop("bottlenecks-top")}` }),
  upload: () => {
    const blob = new Blob([$("upload-csv").value], { type: "text/csv" });
    const body = new FormData();
    body.append("file", blob, "upload.csv");
    return { method: "POST", url: "/api/v1/event-logs", body };
  },
  apikey: () => ({ method: "POST", url: "/api/v1/api-keys" }),
};

function enc(s) { return encodeURIComponent(s); }
function clampTop(id) { const n = parseInt($(id).value, 10); return Number.isFinite(n) && n > 0 ? n : 5; }

async function run(name, panel) {
  const { method, url, body, headers } = actions[name]();
  panel.hidden = false;
  panel.className = "response pending";
  panel.textContent = `${method} ${url}\n\n… calling`;

  const opts = { method, headers: { ...(headers || {}) } };
  const key = apiKey();
  if (key) opts.headers["X-Api-Key"] = key;
  if (body) opts.body = body;

  try {
    const res = await fetch(url, opts);
    const raw = await res.text();
    let pretty = raw;
    try { pretty = JSON.stringify(JSON.parse(raw), null, 2); } catch { /* text formats (mermaid/dot) */ }
    panel.className = "response " + (res.ok ? "ok" : "err");
    panel.textContent = `${method} ${url}\n${res.status} ${res.statusText}\n\n${pretty}`;
  } catch (err) {
    panel.className = "response err";
    panel.textContent = `${method} ${url}\n\nNetwork error: ${err.message}`;
  }
}

document.querySelectorAll("[data-try]").forEach((btn) => {
  btn.addEventListener("click", () => {
    const name = btn.getAttribute("data-try");
    const panel = document.querySelector(`[data-resp="${name}"]`);
    run(name, panel);
  });
});

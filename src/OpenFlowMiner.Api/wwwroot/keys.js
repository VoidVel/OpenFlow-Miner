// Self-serve API key generation. One POST, key shown once. No account, no login.

const $ = (id) => document.getElementById(id);
let currentKey = null;

$("generate").addEventListener("click", async () => {
  const btn = $("generate");
  btn.disabled = true;
  btn.textContent = "Generating…";
  try {
    const res = await fetch("/api/v1/api-keys", { method: "POST" });
    const data = await res.json();
    if (!res.ok) throw new Error(data.detail || data.title || `HTTP ${res.status}`);

    currentKey = data.key;
    $("key-value").value = data.key;
    $("key-limit").textContent = `${data.rateLimit.permitLimit.toLocaleString()} requests / ${data.rateLimit.windowSeconds}s`;
    $("key-expires").textContent = data.expiresAt ? new Date(data.expiresAt).toLocaleString() : "never";
    $("key-result").hidden = false;
    $("test-card").hidden = false;
  } catch (err) {
    alert("Could not generate a key: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "Generate another key";
  }
});

$("copy-key").addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText($("key-value").value);
    const b = $("copy-key");
    b.textContent = "Copied ✓";
    setTimeout(() => (b.textContent = "Copy"), 1500);
  } catch {
    $("key-value").select();
  }
});

$("test-key").addEventListener("click", async () => {
  const panel = $("test-resp");
  panel.hidden = false;
  panel.className = "response pending";
  panel.textContent = "GET /api/v1/event-logs/receipt (with X-Api-Key)\n\n… calling";
  try {
    const res = await fetch("/api/v1/event-logs/receipt", { headers: { "X-Api-Key": currentKey || "" } });
    const pretty = JSON.stringify(await res.json(), null, 2);
    panel.className = "response " + (res.ok ? "ok" : "err");
    panel.textContent = `GET /api/v1/event-logs/receipt  (with X-Api-Key)\n${res.status} ${res.statusText}\n\n${pretty}`;
  } catch (err) {
    panel.className = "response err";
    panel.textContent = "Network error: " + err.message;
  }
});

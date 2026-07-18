// Theme toggle. The initial theme is applied by a tiny inline script in each page's <head> (to avoid
// a flash); this only handles the click and persists the choice.
(function () {
  const btn = document.getElementById("theme-toggle");
  if (!btn) return;
  btn.addEventListener("click", () => {
    const current = document.documentElement.getAttribute("data-theme") === "light" ? "light" : "dark";
    const next = current === "light" ? "dark" : "light";
    document.documentElement.setAttribute("data-theme", next);
    try { localStorage.setItem("ofm-theme", next); } catch (e) { /* ignore */ }
    document.dispatchEvent(new CustomEvent("themechange", { detail: next }));
  });
})();

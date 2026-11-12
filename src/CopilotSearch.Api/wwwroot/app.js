const $ = (id) => document.getElementById(id);
const els = {
  form: $("form"), query: $("query"), go: $("go"), provider: $("provider"), examples: $("examples"),
  error: $("error"), progress: $("progress"), steps: $("steps"), status: $("status"),
  answerPanel: $("answerPanel"), answer: $("answer"), citations: $("citations"), meta: $("meta"),
  resultsPanel: $("resultsPanel"), tabs: $("tabs"), results: $("results"),
};

const SOURCE_LABELS = { all: "All", mail: "Mail", teams: "Teams", files: "Files", copilot_chat: "Copilot chats", calendar: "Calendar" };
let allResults = [];
let citedIds = new Set();
let activeTab = "all";

init();

async function init() {
  try {
    const config = await (await fetch("/config")).json();
    for (const p of config.providers) {
      const opt = new Option(p.configured ? p.name : `${p.name} (not configured)`, p.name);
      opt.disabled = !p.configured;
      els.provider.add(opt);
    }
    els.provider.value = config.provider;
  } catch {
    els.provider.add(new Option("Demo", "Demo"));
  }
}

els.form.addEventListener("submit", (e) => { e.preventDefault(); runSearch(els.query.value.trim()); });
els.examples.addEventListener("click", (e) => {
  if (e.target.tagName !== "BUTTON") return;
  els.query.value = e.target.textContent;
  runSearch(els.query.value);
});

async function runSearch(query) {
  if (!query) return;
  reset();
  els.go.disabled = true;
  els.progress.hidden = false;
  setStatus("Searching…");

  try {
    const res = await fetch("/search", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ query, provider: els.provider.value, options: { max_results: 25 } }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `Request failed (${res.status})`);
    }
    await readSse(res.body, handleEvent);
  } catch (err) {
    showError(err.message);
    setStatus("");
  } finally {
    els.go.disabled = false;
  }
}

function reset() {
  els.error.hidden = true;
  els.steps.innerHTML = "";
  els.answer.textContent = "";
  els.citations.innerHTML = "";
  els.meta.textContent = "";
  els.answerPanel.hidden = true;
  els.resultsPanel.hidden = true;
  allResults = [];
  citedIds = new Set();
  activeTab = "all";
}

async function readSse(body, onEvent) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    let idx;
    while ((idx = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      let name = "message", data = "";
      for (const line of block.split("\n")) {
        if (line.startsWith("event:")) name = line.slice(6).trim();
        else if (line.startsWith("data:")) data += line.slice(5).trim();
      }
      if (data) onEvent(name, JSON.parse(data));
    }
  }
}

const stepEls = new Map();
let rawAnswer = "";

function handleEvent(name, d) {
  switch (name) {
    case "run_started":
      stepEls.clear();
      rawAnswer = "";
      els.meta.textContent = `${d.provider} · ${d.model}`;
      break;
    case "tool_call": {
      const li = document.createElement("li");
      li.className = "running";
      li.innerHTML = `<span class="dot"></span><span class="tool"></span><span class="args"></span><span class="timing"></span>`;
      li.querySelector(".tool").textContent = d.tool;
      li.querySelector(".args").textContent = formatArgs(d.arguments);
      els.steps.append(li);
      stepEls.set(d.call_id, li);
      setStatus("Calling tools…");
      break;
    }
    case "tool_result": {
      const li = stepEls.get(d.call_id);
      if (!li) break;
      li.className = d.ok ? "done" : "failed";
      const detail = d.ok ? (d.returned != null ? `${d.returned} results` : "ok") : `error: ${d.error_code}`;
      li.querySelector(".timing").textContent = `${detail} · ${d.duration_ms} ms`;
      break;
    }
    case "answer_delta":
      rawAnswer += d.text;
      els.answerPanel.hidden = false;
      els.answer.textContent = rawAnswer;
      setStatus("Writing answer…");
      break;
    case "results":
      allResults = d.results;
      break;
    case "final":
      citedIds = new Set(d.citations.map((c) => c.id));
      renderAnswer(d.answer, d.citations);
      renderResults();
      els.meta.textContent += ` · ${(d.duration_ms / 1000).toFixed(1)}s · ${d.usage.input_tokens + d.usage.output_tokens} tokens`;
      setStatus("Done");
      break;
    case "error":
      showError(d.message);
      setStatus("");
      break;
  }
}

function formatArgs(args) {
  return Object.entries(args || {}).filter(([, v]) => v !== null && v !== undefined)
    .map(([k, v]) => `${k}=${typeof v === "string" ? JSON.stringify(v) : JSON.stringify(v)}`).join(" ");
}

function renderAnswer(text, citations) {
  els.answerPanel.hidden = false;
  els.answer.innerHTML = "";
  const byNumber = new Map(citations.map((c) => [c.n, c]));
  const parts = text.split(/(\[\d+\])/g);
  for (const part of parts) {
    const m = /^\[(\d+)\]$/.exec(part);
    const cite = m && byNumber.get(Number(m[1]));
    if (cite) {
      const a = document.createElement("a");
      a.className = "cite";
      a.textContent = cite.n;
      a.title = cite.title;
      a.href = cite.url || "#";
      a.target = "_blank";
      els.answer.append(a);
    } else {
      els.answer.append(renderInline(part));
    }
  }

  els.citations.innerHTML = "";
  for (const c of citations) {
    const a = document.createElement("a");
    a.textContent = `[${c.n}] ${c.title} (${SOURCE_LABELS[c.source] || c.source})`;
    a.href = c.url || "#";
    a.target = "_blank";
    els.citations.append(a);
  }
}

// Minimal **bold** support; everything else stays plain text.
function renderInline(text) {
  const frag = document.createDocumentFragment();
  text.split(/(\*\*[^*]+\*\*)/g).forEach((seg) => {
    if (/^\*\*[^*]+\*\*$/.test(seg)) {
      const b = document.createElement("strong");
      b.textContent = seg.slice(2, -2);
      frag.append(b);
    } else {
      frag.append(document.createTextNode(seg));
    }
  });
  return frag;
}

function renderResults() {
  if (!allResults.length) { els.resultsPanel.hidden = true; return; }
  els.resultsPanel.hidden = false;

  const counts = { all: allResults.length };
  for (const r of allResults) counts[r.source] = (counts[r.source] || 0) + 1;

  els.tabs.innerHTML = "";
  for (const key of Object.keys(counts)) {
    const b = document.createElement("button");
    b.textContent = `${SOURCE_LABELS[key] || key} (${counts[key]})`;
    b.className = key === activeTab ? "active" : "";
    b.onclick = () => { activeTab = key; renderResults(); };
    els.tabs.append(b);
  }

  els.results.innerHTML = "";
  for (const r of allResults.filter((x) => activeTab === "all" || x.source === activeTab)) {
    const li = document.createElement("li");
    if (citedIds.has(r.id)) li.className = "cited";

    const head = document.createElement("div");
    const src = document.createElement("span");
    src.className = `src ${r.source}`;
    src.textContent = SOURCE_LABELS[r.source] || r.source;
    const title = document.createElement("a");
    title.className = "title";
    title.textContent = r.title;
    title.href = r.url || "#";
    title.target = "_blank";
    head.append(src, title);

    const snippet = document.createElement("div");
    snippet.className = "snippet";
    snippet.textContent = r.snippet;

    const sub = document.createElement("div");
    sub.className = "sub";
    const when = r.modified_at || r.created_at;
    sub.textContent = [r.author?.name, when && new Date(when).toLocaleString()].filter(Boolean).join(" · ");

    li.append(head, snippet, sub);
    els.results.append(li);
  }
}

function setStatus(text) { els.status.textContent = text; }
function showError(message) { els.error.textContent = message; els.error.hidden = false; }

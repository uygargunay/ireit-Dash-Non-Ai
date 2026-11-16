const loadButton = document.getElementById("load-submissions");
const list = document.getElementById("admin-list");
const errorBox = document.getElementById("admin-error");

localStorage.removeItem("irei-admin-key");

loadButton.addEventListener("click", loadSubmissions);

async function loadSubmissions() {
  hideError();
  loadButton.disabled = true;
  loadButton.textContent = "Loading…";

  try {
    const response = await fetch("/api/admin/submissions", {
      credentials: "omit"
    });

    if (!response.ok) {
      throw new Error("Could not load submissions.");
    }

    const records = await response.json();
    render(records);
  } catch (error) {
    showError(error.message);
  } finally {
    loadButton.disabled = false;
    loadButton.textContent = "Load submissions";
  }
}

function render(records) {
  if (!records.length) {
    list.className = "admin-list empty-state";
    list.textContent = "No submissions have been processed.";
    return;
  }

  list.className = "admin-list";
  list.innerHTML = records.map(record => `
    <article class="admin-card">
      <div>
        <strong>${escapeHtml(record.organizationName)}</strong>
        <p>${escapeHtml(record.originalFileName)} · ${escapeHtml(record.versionId || "Unversioned")} · ${escapeHtml(record.profileName || "Unclassified")}</p>
        <div class="admin-meta">
          <span>${escapeHtml(record.status)}</span>
          <span>${escapeHtml(record.versionStatus || "—")}</span>
          <span>${new Date(record.createdUtc).toLocaleString()}</span>
          <span>${record.warnings?.length || 0} warning(s)</span>
        </div>
      </div>
      <button type="button" class="button secondary" data-download="${record.id}">Download workbook</button>
    </article>
  `).join("");

  list.querySelectorAll("[data-download]").forEach(button => {
    button.addEventListener("click", () =>
      downloadWorkbook(button.dataset.download, button));
  });
}

async function downloadWorkbook(id, button) {
  const original = button.textContent;
  button.disabled = true;
  button.textContent = "Preparing…";

  try {
    const response = await fetch(
      `/api/admin/submissions/${encodeURIComponent(id)}/output`,
      { credentials: "omit" }
    );

    if (!response.ok) {
      throw new Error("The generated workbook could not be downloaded.");
    }

    const blob = await response.blob();
    const disposition = response.headers.get("content-disposition") || "";
    const match = disposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
    const filename = decodeURIComponent(match?.[1] || `IREI-${id}.xlsx`);

    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  } catch (error) {
    showError(error.message);
  } finally {
    button.disabled = false;
    button.textContent = original;
  }
}

function showError(message) {
  errorBox.textContent = message;
  errorBox.classList.remove("hidden");
}

function hideError() {
  errorBox.classList.add("hidden");
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

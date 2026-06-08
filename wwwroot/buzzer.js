// Stateless buzzer client.
//
// Replaces the old Blazor Server circuit: the lead gate and the buzz are plain
// fetch() POSTs to /api/lead-capture and /api/buzz. No /_blazor, no per-user
// server circuit — so the page scales without saturating the App Runner instance.
//
// Keep the DOM ids/classes and the success text in sync with Components/Pages/Home.razor
// and the load-test harness (concurrency-tests/).

// Mirror of the server-side business-email regex in Program.cs.
const BUSINESS_EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

const LEAD_FIELD_IDS = [
  "firstName",
  "lastName",
  "businessEmail",
  "companyName",
  "jobTitle",
  "country",
];

const $ = (id) => document.getElementById(id);

function showStatus(kind, message) {
  const el = $("buzzStatus");
  if (!el) return;
  el.className = `status ${kind}`; // kind = "ok" | "error"
  el.textContent = message;
  el.hidden = false;
}

function showLeadError(message) {
  const el = $("leadError");
  if (!el) return;
  el.textContent = message;
  el.hidden = !message;
}

/** Reads a JSON error payload's `error` field, falling back to a generic message. */
async function readError(response, fallback) {
  try {
    const body = await response.json();
    if (body && typeof body.error === "string" && body.error) return body.error;
  } catch {
    /* non-JSON body */
  }
  return fallback;
}

async function submitLead() {
  showLeadError("");
  const values = Object.fromEntries(
    LEAD_FIELD_IDS.map((id) => [id, ($(id)?.value ?? "").trim()]),
  );

  if (LEAD_FIELD_IDS.some((id) => !values[id])) {
    showLeadError("All fields are required.");
    return;
  }
  if (!BUSINESS_EMAIL.test(values.businessEmail)) {
    showLeadError("Enter a valid business email address.");
    return;
  }

  const button = $("leadSubmit");
  button.disabled = true;
  button.textContent = "Submitting...";
  try {
    const res = await fetch("/api/lead-capture", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        firstName: values.firstName,
        lastName: values.lastName,
        businessEmailAddress: values.businessEmail,
        companyName: values.companyName,
        jobTitle: values.jobTitle,
        country: values.country,
      }),
    });
    if (!res.ok) {
      showLeadError(await readError(res, `Could not submit details (${res.status}).`));
      return;
    }
    unlockBuzzer();
  } catch (err) {
    showLeadError(`Could not submit details: ${err}`);
  } finally {
    button.disabled = false;
    button.textContent = "Continue To Buzzer";
  }
}

function unlockBuzzer() {
  const gate = $("leadGate");
  if (gate) gate.hidden = true;
  const shell = $("buzzerShell");
  if (shell) {
    shell.classList.remove("is-locked");
    shell.setAttribute("aria-hidden", "false");
  }
  $("teamName")?.focus();
}

async function buzz() {
  const team = ($("teamName")?.value ?? "").trim();
  if (!team) {
    showStatus("error", "Enter a team name before buzzing.");
    return;
  }

  const button = $("buzzButton");
  button.disabled = true;
  button.textContent = "Buzzing...";
  try {
    // Forward ?chaos=/latencyMs= so the observability failure-injection demo works.
    const res = await fetch(`/api/buzz${location.search}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ teamName: team }),
    });
    if (!res.ok) {
      showStatus("error", await readError(res, `Could not send buzz event (${res.status}).`));
      return;
    }
    // Local timestamp so the player can validate exactly when their buzz registered.
    const at = new Date().toLocaleString(undefined, {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
    showStatus("ok", `Buzz received for ${team} at ${at}.`);
  } catch (err) {
    showStatus("error", `Could not send buzz event: ${err}`);
  } finally {
    button.disabled = false;
    button.textContent = "BUZZ";
  }
}

$("leadSubmit")?.addEventListener("click", submitLead);
$("buzzButton")?.addEventListener("click", buzz);

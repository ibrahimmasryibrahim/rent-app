"use strict";

/* ---------------- helpers (display formatting only — all math is server-side) ---------------- */

function fmtMoney(fils) {
  const v = (fils || 0) / 1000;
  return v.toLocaleString("en-US", { minimumFractionDigits: 3, maximumFractionDigits: 3 }) + " د.ك";
}
function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function statusWord(cls) {
  return cls === "pay" ? "عليك" : cls === "get" ? "لك" : "متعادل";
}
function statusWordThirdPerson(cls) {
  return cls === "pay" ? "عليه" : cls === "get" ? "له" : "متعادل";
}
function initials(name, phone) {
  const n = (name || "").trim();
  if (n) return n[0];
  return (phone || "؟").slice(-2);
}
function relativeDay(iso) {
  const d = new Date(iso + (iso.endsWith("Z") ? "" : "Z"));
  const now = new Date();
  const days = Math.floor((now.setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 86400000);
  if (days === 0) return "اليوم";
  if (days === 1) return "أمس";
  return d.toLocaleDateString("ar-KW-u-nu-latn", { year: "numeric", month: "long", day: "numeric" });
}
function timeAgo(iso) {
  const d = new Date(iso + (iso.endsWith("Z") ? "" : "Z"));
  const diffMin = Math.floor((Date.now() - d.getTime()) / 60000);
  if (diffMin < 1) return "الآن";
  if (diffMin < 60) return diffMin + " د";
  if (diffMin < 1440) return Math.floor(diffMin / 60) + " س";
  return Math.floor(diffMin / 1440) + " يوم";
}

function toast(msg) {
  const el = document.getElementById("toastRoot");
  el.textContent = msg;
  el.classList.remove("show");
  void el.offsetWidth;
  el.classList.add("show");
  clearTimeout(toast._t);
  toast._t = setTimeout(() => el.classList.remove("show"), 2400);
}

function closeModal() {
  document.getElementById("modalRoot").innerHTML = "";
}

/** Wraps an async click handler with a spinner + disabled state on the button,
 * so slow requests (free-tier cold starts, weak signal) give feedback instead
 * of looking frozen or inviting a confusing double-tap. */
async function withLoading(btn, fn) {
  if (!btn || btn.classList.contains("loading")) return;
  btn.classList.add("loading");
  btn.disabled = true;
  try {
    await fn();
  } finally {
    btn.classList.remove("loading");
    btn.disabled = false;
  }
}

const SKELETON_SCREEN = `
  <div class="screen">
    <div class="skeleton skel-card"></div>
    <div class="skeleton skel-row"></div>
    <div class="skeleton skel-row"></div>
    <div class="skeleton skel-row"></div>
  </div>`;
function openSheet(html, onMount) {
  const root = document.getElementById("modalRoot");
  root.innerHTML = `<div class="modal-overlay" id="modalOverlay"><div class="modal-sheet"><div class="drag-bar"></div>${html}</div></div>`;
  document.getElementById("modalOverlay").addEventListener("click", (e) => {
    if (e.target.id === "modalOverlay") closeModal();
  });
  if (onMount) onMount(root);
}

/* ---------------- app state ---------------- */

const App = {
  me: null,
  groups: [],
  currentGroupId: null,
  currentGroup: null,
  myRole: null,
  socket: null,
  route: "login"
};

function currentMembership() {
  return App.groups.find((g) => g.id === App.currentGroupId);
}

/* ---------------- socket realtime ---------------- */

function connectSocket() {
  if (App.socket) App.socket.disconnect();
  const { accessToken } = Api.getTokens();
  if (!accessToken) return;
  App.socket = io({ auth: { token: accessToken } });
  App.socket.on("connect", () => {
    if (App.currentGroupId) App.socket.emit("join-group", App.currentGroupId);
  });
  App.socket.on("notification", (n) => {
    toast("🔔 " + n.title);
    refreshBellBadge();
    if (App.route === "notifications") renderRoute();
  });
  ["ExpenseAdded", "ExpenseUpdated", "ExpenseDeleted", "RentUpdated", "MemberAdded", "MemberSuspended",
    "MemberReactivated", "MemberDeleted", "MembersReordered", "BalanceChanged", "MonthClosed", "NewMonthStarted"
  ].forEach((evt) => {
    App.socket.on(evt, () => {
      if (["home", "expenses", "admin", "admin-members"].includes(App.route)) renderRoute();
    });
  });
}

async function refreshBellBadge() {
  try {
    const data = await Api.get("/notifications?limit=1");
    const badge = document.getElementById("bellBadge");
    if (data.unreadCount > 0) {
      badge.textContent = data.unreadCount > 99 ? "99+" : data.unreadCount;
      badge.classList.remove("hidden");
    } else {
      badge.classList.add("hidden");
    }
  } catch (e) { /* ignore */ }
}

/* ---------------- offline detection ---------------- */

function updateOfflineBanner() {
  document.getElementById("offlineBanner").classList.toggle("show", !navigator.onLine);
}
window.addEventListener("online", updateOfflineBanner);
window.addEventListener("offline", updateOfflineBanner);

/* ---------------- router ---------------- */

const routes = {};
function screen(name, fn) { routes[name] = fn; }

async function renderRoute() {
  const hash = location.hash.replace(/^#\/?/, "") || "login";
  const parts = hash.split("/");
  const routeKey = parts[0] === "admin" && parts[1] ? "admin/" + parts[1] : parts[0];
  App.route = parts[0]; // "admin" for any admin sub-page, used for bottom-nav active state
  updateChrome();
  const root = document.getElementById("screenRoot");
  root.innerHTML = ["login", "onboarding"].includes(routeKey) ? "" : SKELETON_SCREEN;
  try {
    const fn = routes[routeKey] || routes["login"];
    await fn(root, parts.slice(routeKey.includes("/") ? 2 : 1));
  } catch (e) {
    if (e.status === 401) return;
    root.innerHTML = `<div class="screen"><div class="empty-hint">⚠ ${escapeHtml(e.message || "حدث خطأ")}</div></div>`;
  }
}
window.addEventListener("hashchange", renderRoute);

function updateChrome() {
  const authed = !!Api.getTokens().accessToken && !!App.me;
  const inGroupScreens = ["home", "expenses", "notifications", "admin", "profile"].includes(App.route) ||
    App.route.startsWith("admin");
  document.getElementById("appHeader").classList.toggle("hidden", !authed || !inGroupScreens);
  document.getElementById("bottomNav").classList.toggle("hidden", !authed || !App.currentGroupId || !inGroupScreens);
  document.getElementById("navAdmin").classList.toggle("hidden", App.myRole !== "admin");
  if (App.me) {
    document.getElementById("headerGreet").textContent = App.me.name || App.me.phone;
    document.getElementById("headerSub").textContent = App.currentGroup ? App.currentGroup.name : "";
  }
  document.querySelectorAll("#bottomNav button[data-route]").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.route === App.route || (App.route.startsWith("admin") && btn.dataset.route === "admin"));
  });
}

document.getElementById("bottomNav").addEventListener("click", (e) => {
  const btn = e.target.closest("button[data-route]");
  if (btn) location.hash = "#/" + btn.dataset.route;
});
document.getElementById("bellBtn").addEventListener("click", () => (location.hash = "#/notifications"));

/* ================= SCREEN: login ================= */
// No verification code: the real gate is the group admin approving your
// join request (see the onboarding screen). Logging in only establishes
// who you're claiming to be; it never grants access to any group's data.

screen("login", (root) => {
  root.innerHTML = `
    <div class="center-screen">
      <div class="brand-hero">
        <div class="emoji">🏠</div>
        <h1>حاسبة السكن المشترك</h1>
        <p>اكتب اسمك ورقم هاتفك للمتابعة</p>
      </div>
      <div class="field">
        <label for="nameInput">اسمك</label>
        <input id="nameInput" type="text" placeholder="مثال: أحمد" autocomplete="name">
      </div>
      <div class="field">
        <label for="phoneInput">رقم الهاتف</label>
        <input id="phoneInput" type="tel" placeholder="+965XXXXXXXX" autocomplete="tel" dir="ltr">
        <div class="error" id="phoneError"></div>
      </div>
      <button class="btn btn-primary btn-block" id="loginBtn">دخول</button>
    </div>`;
  const loginBtn = document.getElementById("loginBtn");
  loginBtn.addEventListener("click", () => withLoading(loginBtn, async () => {
    const name = document.getElementById("nameInput").value.trim();
    const phone = document.getElementById("phoneInput").value.trim();
    const errEl = document.getElementById("phoneError");
    errEl.classList.remove("show");
    try {
      const res = await Api.post("/auth/login", { phone, name }, { noAuth: true });
      Api.setTokens(res.accessToken, res.refreshToken);
      Api.setMe(res.user);
      App.me = res.user;
      await afterLogin();
    } catch (e) {
      errEl.textContent = e.message;
      errEl.classList.add("show");
    }
  }));
});

async function afterLogin() {
  connectSocket();
  const data = await Api.get("/groups/mine");
  App.groups = data.groups;
  if (App.groups.length === 0) {
    location.hash = "#/onboarding";
  } else {
    const saved = localStorage.getItem("currentGroupId");
    const found = App.groups.find((g) => g.id === saved);
    await selectGroup(found ? found.id : App.groups[0].id);
    location.hash = "#/home";
  }
}

async function selectGroup(groupId) {
  App.currentGroupId = groupId;
  localStorage.setItem("currentGroupId", groupId);
  const g = App.groups.find((x) => x.id === groupId);
  App.currentGroup = g;
  App.myRole = g ? g.role : null;
  if (App.socket && App.socket.connected) App.socket.emit("join-group", groupId);
  refreshBellBadge();
}

/* ================= SCREEN: onboarding ================= */

screen("onboarding", (root) => {
  root.innerHTML = `
    <div class="center-screen">
      <div class="brand-hero">
        <div class="emoji">🏡</div>
        <h1>ابدأ الآن</h1>
        <p>أنشئ مجموعة سكن جديدة أو انضم لمجموعة موجودة</p>
      </div>
      <div class="tabs">
        <button class="active" data-tab="create">إنشاء مجموعة</button>
        <button data-tab="join">الانضمام بكود</button>
      </div>

      <div class="tab-panel active" data-panel="create">
        <div class="field">
          <label for="groupName">اسم المجموعة</label>
          <input id="groupName" placeholder="مثال: سكن الشباب">
        </div>
        <button class="btn btn-primary btn-block" id="createGroupBtn">+ إنشاء مجموعة</button>
      </div>

      <div class="tab-panel" data-panel="join">
        <div class="field">
          <label for="inviteCode">كود الدعوة</label>
          <input id="inviteCode" placeholder="ABC123" dir="ltr" style="text-transform:uppercase">
          <div class="hint">هتحتاج الكود من مشرف المجموعة، وطلبك هيحتاج موافقته</div>
        </div>
        <button class="btn btn-primary btn-block" id="joinGroupBtn">انضمام</button>
      </div>

      <div class="error" id="onboardError" style="text-align:center;margin-top:10px"></div>
    </div>`;

  root.querySelectorAll(".tabs button").forEach((tabBtn) => {
    tabBtn.addEventListener("click", () => {
      root.querySelectorAll(".tabs button").forEach((b) => b.classList.toggle("active", b === tabBtn));
      root.querySelectorAll(".tab-panel").forEach((p) => p.classList.toggle("active", p.dataset.panel === tabBtn.dataset.tab));
      document.getElementById("onboardError").classList.remove("show");
    });
  });

  const errEl = document.getElementById("onboardError");
  const createBtn = document.getElementById("createGroupBtn");
  createBtn.addEventListener("click", () => withLoading(createBtn, async () => {
    const name = document.getElementById("groupName").value.trim();
    errEl.classList.remove("show");
    if (!name) { errEl.textContent = "اكتب اسم المجموعة"; errEl.classList.add("show"); return; }
    try {
      await Api.post("/groups", { name });
      const data = await Api.get("/groups/mine");
      App.groups = data.groups;
      await selectGroup(App.groups[App.groups.length - 1].id);
      location.hash = "#/home";
    } catch (e) {
      errEl.textContent = e.message; errEl.classList.add("show");
    }
  }));

  const joinBtn = document.getElementById("joinGroupBtn");
  joinBtn.addEventListener("click", () => withLoading(joinBtn, async () => {
    const inviteCode = document.getElementById("inviteCode").value.trim();
    errEl.classList.remove("show");
    try {
      const res = await Api.post("/groups/join", { inviteCode });
      if (res.status === "pending") {
        toast("تم إرسال طلب الانضمام — بانتظار موافقة المشرف");
      } else {
        const data = await Api.get("/groups/mine");
        App.groups = data.groups;
        await selectGroup(res.groupId);
        location.hash = "#/home";
      }
    } catch (e) {
      errEl.textContent = e.message; errEl.classList.add("show");
    }
  }));
});

/* ================= SCREEN: home ================= */

screen("home", async (root) => {
  const dash = await Api.get(`/groups/${App.currentGroupId}/dashboard`);
  const membersData = await Api.get(`/groups/${App.currentGroupId}/members`);
  const cls = dash.me ? dash.me.status : "even";

  root.innerHTML = `
    <div class="screen">
      <div class="status-card ${cls}" id="statusCard">
        <div class="sc-label">حالتك المالية</div>
        <div class="sc-amount">${dash.me ? fmtMoney(Math.abs(dash.me.dueFils)) : "—"}</div>
        <div class="sc-hint">${dash.me ? (cls === "pay" ? "عليك دفع هذا المبلغ" : cls === "get" ? "لك هذا المبلغ" : "أنت متعادل") : "لست ضمن الحساب الحالي (معلّق)"}</div>
      </div>

      <div class="mini-stats">
        <div class="mini-stat"><div class="label">نصيبي من الإيجار</div><div class="value">${dash.me ? fmtMoney(dash.me.rentShareFils) : "—"}</div></div>
        <div class="mini-stat"><div class="label">مصروفاتي</div><div class="value">${dash.me ? fmtMoney(dash.me.paidFils) : "—"}</div></div>
        <div class="mini-stat"><div class="label">الفرق</div><div class="value">${dash.me ? fmtMoney(Math.abs(dash.me.dueFils)) : "—"}</div></div>
      </div>

      <div class="section-title">أعضاء المجموعة <span class="link">${dash.membersCount} نشط</span></div>
      <div class="card-list" id="membersList"></div>

      <div class="section-title">أحدث المصروفات <span class="link" id="seeAllExpenses">عرض الكل</span></div>
      <div class="card-list" id="recentExpenses"></div>
    </div>`;

  document.getElementById("seeAllExpenses").addEventListener("click", () => (location.hash = "#/expenses"));

  const membersList = document.getElementById("membersList");
  membersList.innerHTML = membersData.members.filter((m) => m.status === "active").map(memberCardHtml).join("") ||
    `<div class="empty-hint">لا يوجد أعضاء بعد</div>`;
  membersList.querySelectorAll("[data-membership]").forEach((el) => {
    el.addEventListener("click", () => openMemberDetail(el.dataset.membership));
  });

  const recent = document.getElementById("recentExpenses");
  recent.innerHTML = dash.recentExpenses.length
    ? dash.recentExpenses.map(expenseItemHtml).join("")
    : `<div class="empty-hint">لا توجد مصروفات بعد</div>`;
});

function memberCardHtml(m) {
  const pillCls = m.status === "suspended" ? "suspended" : (m.status_financial || "even");
  const pillText = m.status === "suspended"
    ? "🟡 معلّق"
    : (m.status_financial === "even" ? "متعادل" : `${statusWordThirdPerson(m.status_financial)} ${fmtMoney(Math.abs(m.dueFils))}`);
  return `
    <div class="member-card" data-membership="${m.membershipId}">
      <div class="avatar">${escapeHtml(initials(m.name, m.phone))}</div>
      <div class="info">
        <div class="name">${escapeHtml(m.name || m.phone)}</div>
        <div class="sub">${m.role === "admin" ? "مشرف" : "عضو"}</div>
      </div>
      <span class="status-pill ${pillCls}">${pillText}</span>
    </div>`;
}

function expenseItemHtml(e) {
  return `
    <div class="expense-item">
      <div class="icon">🧾</div>
      <div class="info">
        <div class="name">${escapeHtml(e.name || e.category)}</div>
        <div class="meta">${escapeHtml(e.paidByName || e.paidByUserId)} · ${timeAgo(e.createdAt || e.created_at)}</div>
      </div>
      <div class="amount">${fmtMoney(e.amountFils || e.amount_fils)}</div>
    </div>`;
}

async function openMemberDetail(membershipId) {
  const detail = await Api.get(`/groups/${App.currentGroupId}/members/${membershipId}/detail`);
  let body;
  if (!detail.stats) {
    body = `<p style="color:var(--text-soft);font-size:.85rem">هذا الشخص معلّق حاليًا ولا يشارك في حسابات هذا الشهر.</p>`;
  } else {
    const s = detail.stats;
    const lines = detail.expenses;
    body = `
      <div class="detail-list">
        <div class="detail-row"><span>نصيب الإيجار</span><b>${fmtMoney(s.rentShareFils)}</b></div>
        <div class="detail-row"><span>نصيب المصروفات</span><b>${fmtMoney(s.expenseShareFils)}</b></div>
        <div class="detail-row total"><span>إجمالي المستحق عليه</span><b>${fmtMoney(s.fairShareFils)}</b></div>
      </div>
      <div class="section-title" style="margin-top:10px">مصروفاته</div>
      <div class="detail-list">
        ${lines.length ? lines.map((l) => `<div class="detail-row"><span>${escapeHtml(l.name)} — ${escapeHtml(l.category)}</span><b>${fmtMoney(l.amountFils)}</b></div>`).join("") : `<div class="empty-hint">لم يدفع أي مصروف بعد</div>`}
        <div class="detail-row total"><span>إجمالي المصروفات</span><b>${fmtMoney(s.paidFils)}</b></div>
      </div>
      <div class="detail-result ${s.statusWord}">
        <span>${fmtMoney(s.fairShareFils)} − ${fmtMoney(s.paidFils)} = ${fmtMoney(Math.abs(s.dueFils))}</span>
        <span class="status-pill ${s.statusWord}">${statusWordThirdPerson(s.statusWord)}</span>
      </div>`;
  }
  openSheet(`<h3>تفاصيل العضو</h3>${body}<div class="modal-actions"><button class="btn btn-block" id="closeDetailBtn">إغلاق</button></div>`, (root) => {
    root.querySelector("#closeDetailBtn").addEventListener("click", closeModal);
  });
}

/* ================= SCREEN: expenses ================= */

screen("expenses", async (root) => {
  const data = await Api.get(`/groups/${App.currentGroupId}/expenses`);
  const grouped = groupByDay(data.expenses);

  root.innerHTML = `
    <div class="screen">
      <div class="section-title">المصروفات</div>
      <div id="expenseFeed">${renderExpenseFeed(grouped, data.expenses.length)}</div>
    </div>
    <button class="fab" id="addExpenseFab" title="إضافة مصروف">+</button>`;

  document.getElementById("addExpenseFab").addEventListener("click", openAddExpenseModal);
  root.querySelectorAll("[data-expense-id]").forEach((el) => {
    el.addEventListener("click", () => openExpenseDetail(data.expenses.find((e) => e.id === el.dataset.expenseId)));
  });
});

function groupByDay(expenses) {
  const map = new Map();
  expenses.forEach((e) => {
    const key = relativeDay(e.createdAt);
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(e);
  });
  return map;
}
function renderExpenseFeed(grouped, total) {
  if (total === 0) return `<div class="empty-hint">لا توجد مصروفات بعد — اضغط + للإضافة</div>`;
  let html = "";
  for (const [day, items] of grouped) {
    html += `<div class="day-divider">${day}</div><div class="card-list">`;
    html += items.map((e) => `
      <div class="expense-item" data-expense-id="${e.id}" style="cursor:pointer">
        <div class="icon">🧾</div>
        <div class="info">
          <div class="name">${escapeHtml(e.name)}</div>
          <div class="meta">${escapeHtml(e.paidByName)} · ${escapeHtml(e.category)}</div>
        </div>
        <div class="amount">${fmtMoney(e.amountFils)}</div>
      </div>`).join("");
    html += `</div>`;
  }
  return html;
}

async function openAddExpenseModal() {
  const members = (await Api.get(`/groups/${App.currentGroupId}/members`)).members.filter((m) => m.status === "active");
  openSheet(`
    <h3>إضافة مصروف</h3>
    <div class="field"><label>اسم المصروف</label><input id="expName" placeholder="مثال: كهرباء"></div>
    <div class="field"><label>المبلغ (د.ك)</label><input id="expAmount" type="number" min="0" step="0.001" inputmode="decimal"></div>
    <div class="field"><label>التصنيف</label>
      <select id="expCategory">
        <option>مواد غذائية</option><option>كهرباء</option><option>ماء</option><option>إنترنت</option><option>تنظيف</option><option>أخرى</option>
      </select>
    </div>
    <div class="field"><label>الشخص الذي دفع</label>
      <select id="expPayer">${members.map((m) => `<option value="${m.userId}">${escapeHtml(m.name || m.phone)}</option>`).join("")}</select>
    </div>
    <div class="error" id="expError"></div>
    <div class="modal-actions">
      <button class="btn" id="cancelExpBtn">إلغاء</button>
      <button class="btn btn-primary" id="saveExpBtn">حفظ</button>
    </div>`, (root) => {
    root.querySelector("#cancelExpBtn").addEventListener("click", closeModal);
    const saveExpBtn = root.querySelector("#saveExpBtn");
    saveExpBtn.addEventListener("click", () => withLoading(saveExpBtn, async () => {
      const name = root.querySelector("#expName").value.trim();
      const amount = root.querySelector("#expAmount").value;
      const category = root.querySelector("#expCategory").value;
      const paidByUserId = root.querySelector("#expPayer").value;
      const errEl = root.querySelector("#expError");
      try {
        await Api.post(`/groups/${App.currentGroupId}/expenses`, { name, amount, category, paidByUserId });
        closeModal();
        toast("تمت إضافة المصروف");
        renderRoute();
      } catch (e) {
        errEl.textContent = e.message; errEl.classList.add("show");
      }
    }));
  });
}

function openExpenseDetail(e) {
  if (!e) return;
  const canEdit = App.myRole === "admin";
  openSheet(`
    <h3>${escapeHtml(e.name)}</h3>
    <div class="detail-list">
      <div class="detail-row"><span>المبلغ</span><b>${fmtMoney(e.amountFils)}</b></div>
      <div class="detail-row"><span>التصنيف</span><b>${escapeHtml(e.category)}</b></div>
      <div class="detail-row"><span>دفعه</span><b>${escapeHtml(e.paidByName)}</b></div>
      <div class="detail-row"><span>أضافه</span><b>${escapeHtml(e.createdByName)}</b></div>
    </div>
    <div class="modal-actions">
      <button class="btn" id="closeExpDetail">إغلاق</button>
      ${canEdit ? `<button class="btn btn-danger" id="deleteExpBtn">حذف</button>` : ""}
    </div>`, (root) => {
    root.querySelector("#closeExpDetail").addEventListener("click", closeModal);
    const delBtn = root.querySelector("#deleteExpBtn");
    if (delBtn) delBtn.addEventListener("click", () => withLoading(delBtn, async () => {
      await Api.del(`/groups/${App.currentGroupId}/expenses/${e.id}`);
      closeModal();
      toast("تم حذف المصروف");
      renderRoute();
    }));
  });
}

/* ================= SCREEN: notifications ================= */

const NOTIF_FILTERS = [
  { key: "", label: "الكل" }, { key: "financial", label: "مالية" }, { key: "expenses", label: "المصروفات" },
  { key: "members", label: "الأعضاء" }, { key: "admin", label: "الإدارة" }, { key: "system", label: "النظام" }
];
let currentNotifFilter = "";

screen("notifications", async (root) => {
  const data = await Api.get(`/notifications${currentNotifFilter ? "?filter=" + currentNotifFilter : ""}`);
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">الإشعارات <span class="link" id="markAllRead">تحديد الكل كمقروء</span></div>
      <div class="chip-row">${NOTIF_FILTERS.map((f) => `<button class="chip ${f.key === currentNotifFilter ? "active" : ""}" data-filter="${f.key}">${f.label}</button>`).join("")}</div>
      <div id="notifList">${data.notifications.length ? data.notifications.map(notifItemHtml).join("") : `<div class="empty-hint">لا توجد إشعارات</div>`}</div>
    </div>`;

  root.querySelectorAll(".chip").forEach((chip) => {
    chip.addEventListener("click", () => { currentNotifFilter = chip.dataset.filter; renderRoute(); });
  });
  document.getElementById("markAllRead").addEventListener("click", async () => {
    await Api.patch("/notifications/read-all");
    refreshBellBadge();
    renderRoute();
  });
  root.querySelectorAll("[data-notif-id]").forEach((el) => {
    el.addEventListener("click", async () => {
      await Api.patch(`/notifications/${el.dataset.notifId}/read`);
      refreshBellBadge();
      const notif = data.notifications.find((n) => n.id === el.dataset.notifId);
      if (notif.related_entity_type === "expense") location.hash = "#/expenses";
      else if (notif.type === "join_request") location.hash = "#/admin/members";
      else renderRoute();
    });
  });
});

const NOTIF_ICONS = {
  expense_added: "🧾", expense_updated: "✏️", expense_deleted: "🗑️", rent_updated: "🏠",
  member_added: "👋", member_suspended: "🟡", member_reactivated: "🟢", join_request: "📥",
  join_approved: "✅", join_rejected: "❌", new_month_started: "🆕", month_closed: "🔒"
};
function notifItemHtml(n) {
  return `
    <div class="notif-item ${n.read_at ? "" : "unread"}" data-notif-id="${n.id}" style="cursor:pointer">
      <div class="ic">${NOTIF_ICONS[n.type] || "🔔"}</div>
      <div class="info">
        <div class="title">${escapeHtml(n.title)}</div>
        <div class="msg">${escapeHtml(n.message)}</div>
        <div class="time">${timeAgo(n.created_at)}</div>
      </div>
    </div>`;
}

/* ================= SCREEN: profile ================= */

screen("profile", (root) => {
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">حسابي</div>
      <div class="field"><label>الاسم</label><input id="profileName" value="${escapeHtml(App.me.name || "")}"></div>
      <div class="field"><label>رقم الهاتف</label><input value="${escapeHtml(App.me.phone)}" disabled dir="ltr"></div>
      <button class="btn btn-primary btn-block" id="saveProfileBtn">حفظ</button>

      <div class="section-title" style="margin-top:26px">المجموعات</div>
      <div class="card-list">
        ${App.groups.map((g) => `
          <div class="member-card" data-switch-group="${g.id}">
            <div class="avatar">${escapeHtml(g.name[0] || "?")}</div>
            <div class="info"><div class="name">${escapeHtml(g.name)}</div><div class="sub">${g.role === "admin" ? "مشرف" : "عضو"} — كود: ${g.invite_code}</div></div>
            ${g.id === App.currentGroupId ? `<span class="status-pill get">الحالية</span>` : ""}
          </div>`).join("")}
      </div>

      <button class="btn btn-danger btn-block" id="logoutBtn" style="margin-top:26px">تسجيل الخروج</button>
    </div>`;

  const saveProfileBtn = document.getElementById("saveProfileBtn");
  saveProfileBtn.addEventListener("click", () => withLoading(saveProfileBtn, async () => {
    const name = document.getElementById("profileName").value.trim();
    const res = await Api.patch("/auth/me", { name });
    App.me = res; Api.setMe(res);
    toast("تم الحفظ");
    updateChrome();
  }));
  root.querySelectorAll("[data-switch-group]").forEach((el) => {
    el.addEventListener("click", async () => {
      await selectGroup(el.dataset.switchGroup);
      location.hash = "#/home";
    });
  });
  document.getElementById("logoutBtn").addEventListener("click", async () => {
    const { refreshToken } = Api.getTokens();
    try { await Api.post("/auth/logout", { refreshToken }); } catch (e) {}
    Api.clearTokens();
    if (App.socket) App.socket.disconnect();
    location.hash = "#/login";
    location.reload();
  });
});

/* ================= SCREEN: admin dashboard ================= */

screen("admin", async (root) => {
  if (App.myRole !== "admin") { root.innerHTML = `<div class="screen"><div class="empty-hint">هذه الصفحة للمشرف فقط</div></div>`; return; }
  const dash = await Api.get(`/groups/${App.currentGroupId}/dashboard`);
  const membersData = await Api.get(`/groups/${App.currentGroupId}/members`);
  const active = membersData.members.filter((m) => m.status === "active").length;
  const suspended = membersData.members.length - active;
  const owed = membersData.members.filter((m) => m.status_financial === "pay").reduce((s, m) => s + m.dueFils, 0);
  const credit = membersData.members.filter((m) => m.status_financial === "get").reduce((s, m) => s - m.dueFils, 0);

  root.innerHTML = `
    <div class="screen">
      <div class="section-title">لوحة المشرف</div>
      <div class="kpi-grid">
        <div class="kpi-card"><div class="label">الإيجار الشهري</div><div class="value">${fmtMoney(dash.group.rent_fils)}</div></div>
        <div class="kpi-card"><div class="label">الأعضاء النشطون</div><div class="value">${active} <span style="font-size:.7rem;color:var(--text-soft)">(${suspended} معلّق)</span></div></div>
        <div class="kpi-card"><div class="label">إجمالي المصروفات</div><div class="value">${fmtMoney(dash.totalExpensesFils)}</div></div>
        <div class="kpi-card"><div class="label">حالة الشهر</div><div class="value" style="font-size:.85rem">${dash.period ? dash.period.label : "—"} · ${dash.period && dash.period.status === "open" ? "مفتوح" : "مغلق"}</div></div>
        <div class="kpi-card"><div class="label">مستحق التحصيل</div><div class="value" style="color:var(--danger)">${fmtMoney(owed)}</div></div>
        <div class="kpi-card"><div class="label">مستحق الاسترداد</div><div class="value" style="color:var(--success)">${fmtMoney(credit)}</div></div>
      </div>

      <div class="admin-menu-item" data-nav="admin/members"><span class="ic">👥</span><span class="label">إدارة الأعضاء</span><span class="chev">‹</span></div>
      <div class="admin-menu-item" data-nav="admin/rent"><span class="ic">🏠</span><span class="label">الإيجار الشهري</span><span class="chev">‹</span></div>
      <div class="admin-menu-item" data-nav="admin/period"><span class="ic">📅</span><span class="label">الشهر المالي</span><span class="chev">‹</span></div>
      <div class="admin-menu-item" data-nav="admin/requests"><span class="ic">📥</span><span class="label">طلبات الانضمام</span><span class="chev">‹</span></div>
      <div class="admin-menu-item" data-nav="admin/audit"><span class="ic">📜</span><span class="label">سجل العمليات</span><span class="chev">‹</span></div>
      <div class="admin-menu-item" data-nav="admin/settings"><span class="ic">⚙️</span><span class="label">إعدادات المجموعة</span><span class="chev">‹</span></div>
    </div>`;
  root.querySelectorAll("[data-nav]").forEach((el) => el.addEventListener("click", () => (location.hash = "#/" + el.dataset.nav)));
});

/* ---- admin/members: full management with drag-drop + action menu ---- */

screen("admin/members", async (root) => {
  const data = await Api.get(`/groups/${App.currentGroupId}/members`);
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">إدارة الأعضاء <span class="link" id="addMemberLink">+ إضافة</span></div>
      <div id="pplList"></div>
    </div>`;
  document.getElementById("addMemberLink").addEventListener("click", openAddMemberModal);
  renderPplList(root, data.members);
});

function renderPplList(root, members) {
  const list = root.querySelector("#pplList");
  list.innerHTML = members.map((m) => `
    <div class="ppl-row" draggable="true" data-mid="${m.membershipId}">
      <span class="drag-handle">⠿</span>
      <div class="avatar" style="width:32px;height:32px;font-size:.78rem">${escapeHtml(initials(m.name, m.phone))}</div>
      <div class="info" style="flex:1">
        <div class="name" style="font-weight:700;font-size:.88rem">${escapeHtml(m.name || m.phone)}</div>
        <div class="sub" style="font-size:.72rem;color:var(--text-soft)">${m.role === "admin" ? "مشرف" : "عضو"} ${m.status === "suspended" ? "· معلّق" : ""}</div>
      </div>
      <button class="btn btn-ghost" data-menu="${m.membershipId}" style="padding:6px 10px">⋮</button>
    </div>`).join("");

  let dragSrc = null;
  list.querySelectorAll(".ppl-row").forEach((row) => {
    row.addEventListener("dragstart", () => { dragSrc = row.dataset.mid; row.classList.add("dragging"); });
    row.addEventListener("dragend", () => row.classList.remove("dragging"));
    row.addEventListener("dragover", (e) => e.preventDefault());
    row.addEventListener("drop", async (e) => {
      e.preventDefault();
      if (!dragSrc || dragSrc === row.dataset.mid) return;
      const ids = Array.from(list.querySelectorAll(".ppl-row")).map((r) => r.dataset.mid);
      const fromIdx = ids.indexOf(dragSrc);
      const toIdx = ids.indexOf(row.dataset.mid);
      ids.splice(fromIdx, 1);
      ids.splice(toIdx, 0, dragSrc);
      await Api.patch(`/groups/${App.currentGroupId}/members/reorder`, { orderedMembershipIds: ids });
      renderRoute();
    });
  });

  list.querySelectorAll("[data-menu]").forEach((btn) => {
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      const m = members.find((x) => x.membershipId === btn.dataset.menu);
      openMemberActionSheet(m);
    });
  });
}

function openMemberActionSheet(m) {
  const isSuspended = m.status === "suspended";
  openSheet(`
    <h3>${escapeHtml(m.name || m.phone)}</h3>
    <div class="card-list">
      <button class="btn btn-block" id="viewDetailAction">ℹ️ عرض التفاصيل</button>
      ${isSuspended
        ? `<button class="btn btn-block" id="reactivateAction">🟢 إعادة تفعيل</button>`
        : `<button class="btn btn-block" id="suspendAction">🟡 تعليق</button>`}
      <button class="btn btn-danger btn-block" id="deleteAction">🗑 حذف نهائي</button>
    </div>`, (root) => {
    root.querySelector("#viewDetailAction").addEventListener("click", () => { closeModal(); openMemberDetail(m.membershipId); });
    const susp = root.querySelector("#suspendAction");
    if (susp) susp.addEventListener("click", async () => {
      await Api.patch(`/groups/${App.currentGroupId}/members/${m.membershipId}/suspend`, {});
      closeModal(); toast("تم تعليق العضو"); renderRoute();
    });
    const react = root.querySelector("#reactivateAction");
    if (react) react.addEventListener("click", () => {
      closeModal();
      confirmDialog("إعادة التفعيل", `هل تريد إعادة "${escapeHtml(m.name || m.phone)}" إلى الحساب الحالي؟`, async () => {
        await Api.patch(`/groups/${App.currentGroupId}/members/${m.membershipId}/reactivate`, {});
        toast("تم تفعيل العضو"); renderRoute();
      });
    });
    root.querySelector("#deleteAction").addEventListener("click", () => {
      closeModal();
      confirmDialog("حذف نهائي", `سيتم حذف "${escapeHtml(m.name || m.phone)}" نهائيًا. هل أنت متأكد؟`, async () => {
        await Api.del(`/groups/${App.currentGroupId}/members/${m.membershipId}`);
        toast("تم حذف العضو"); renderRoute();
      });
    });
  });
}

function confirmDialog(title, message, onConfirm) {
  openSheet(`
    <h3>${escapeHtml(title)}</h3>
    <p style="color:var(--text-soft);font-size:.85rem">${message}</p>
    <div class="modal-actions">
      <button class="btn" id="cancelConfirm">إلغاء</button>
      <button class="btn btn-primary" id="okConfirm">تأكيد</button>
    </div>`, (root) => {
    root.querySelector("#cancelConfirm").addEventListener("click", closeModal);
    root.querySelector("#okConfirm").addEventListener("click", async () => { closeModal(); await onConfirm(); });
  });
}

function openAddMemberModal() {
  openSheet(`
    <h3>إضافة عضو</h3>
    <div class="field"><label>رقم الهاتف</label><input id="newMemberPhone" placeholder="+965XXXXXXXX" dir="ltr"></div>
    <div class="field"><label>الاسم (اختياري)</label><input id="newMemberName"></div>
    <div class="error" id="addMemberError"></div>
    <div class="modal-actions">
      <button class="btn" id="cancelAddMember">إلغاء</button>
      <button class="btn btn-primary" id="saveAddMember">+ إضافة</button>
    </div>`, (root) => {
    root.querySelector("#cancelAddMember").addEventListener("click", closeModal);
    const saveAddMember = root.querySelector("#saveAddMember");
    saveAddMember.addEventListener("click", () => withLoading(saveAddMember, async () => {
      try {
        await Api.post(`/groups/${App.currentGroupId}/members`, {
          phone: root.querySelector("#newMemberPhone").value.trim(),
          name: root.querySelector("#newMemberName").value.trim()
        });
        closeModal(); toast("تمت إضافة العضو"); renderRoute();
      } catch (e) {
        const err = root.querySelector("#addMemberError");
        err.textContent = e.message; err.classList.add("show");
      }
    }));
  });
}

/* ---- admin/rent ---- */

screen("admin/rent", async (root) => {
  const group = (await Api.get(`/groups/${App.currentGroupId}`)).group;
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">الإيجار الشهري</div>
      <div class="field"><label>القيمة الحالية</label><input id="rentInput" type="number" min="0" step="0.001" value="${(group.rent_fils / 1000).toFixed(3)}"></div>
      <div class="hint" style="color:var(--text-soft);font-size:.78rem;margin-bottom:14px">سيُعاد حساب نصيب كل عضو نشط تلقائيًا فور الحفظ</div>
      <button class="btn btn-primary btn-block" id="saveRentBtn">حفظ</button>
    </div>`;
  const saveRentBtn = document.getElementById("saveRentBtn");
  saveRentBtn.addEventListener("click", () => withLoading(saveRentBtn, async () => {
    await Api.patch(`/groups/${App.currentGroupId}/rent`, { rent: document.getElementById("rentInput").value });
    toast("تم تحديث الإيجار"); location.hash = "#/admin";
  }));
});

/* ---- admin/period ---- */

screen("admin/period", async (root) => {
  const [history, current] = await Promise.all([
    Api.get(`/groups/${App.currentGroupId}/period/history`),
    Api.get(`/groups/${App.currentGroupId}/dashboard`)
  ]);
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">الشهر المالي</div>
      <div class="kpi-card" style="margin-bottom:16px">
        <div class="label">الشهر الحالي</div>
        <div class="value">${current.period ? current.period.label : "—"} — ${current.period && current.period.status === "open" ? "مفتوح" : "مغلق"}</div>
      </div>
      <button class="btn btn-block" id="closeMonthBtn" ${!current.period || current.period.status !== "open" ? "disabled" : ""}>إغلاق الشهر الحالي</button>
      <button class="btn btn-primary btn-block" id="newMonthBtn" style="margin-top:8px">↻ بدء شهر جديد</button>

      <div class="section-title" style="margin-top:20px">السجل</div>
      <div class="card-list">
        ${history.periods.map((p) => `
          <div class="member-card" style="cursor:default">
            <div class="info"><div class="name">${escapeHtml(p.label)}</div><div class="sub">الإيجار: ${fmtMoney(p.rent_fils_snapshot)}</div></div>
            <span class="status-pill ${p.status === "open" ? "get" : "even"}">${p.status === "open" ? "مفتوح" : "مغلق"}</span>
          </div>`).join("")}
      </div>
    </div>`;

  document.getElementById("closeMonthBtn").addEventListener("click", () => {
    confirmDialog("إغلاق الشهر", "لن يُسمح بإضافة أو تعديل مصروفات بعد الإغلاق. هل أنت متأكد؟", async () => {
      await Api.post(`/groups/${App.currentGroupId}/period/close`, {});
      toast("تم إغلاق الشهر"); renderRoute();
    });
  });
  document.getElementById("newMonthBtn").addEventListener("click", () => {
    confirmDialog("بدء شهر جديد", "سيتم إغلاق الحساب الحالي وبدء حساب جديد. ستبقى جميع البيانات السابقة محفوظة.", async () => {
      await Api.post(`/groups/${App.currentGroupId}/period/new-month`, {});
      toast("بدأ شهر جديد"); renderRoute();
    });
  });
});

/* ---- admin/requests ---- */

screen("admin/requests", async (root) => {
  const data = await Api.get(`/groups/${App.currentGroupId}/join-requests`);
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">طلبات الانضمام</div>
      <div class="card-list" id="reqList">
        ${data.requests.length ? data.requests.map((r) => `
          <div class="member-card" style="cursor:default">
            <div class="avatar">${escapeHtml(initials(r.name, r.phone))}</div>
            <div class="info"><div class="name">${escapeHtml(r.name || r.phone)}</div></div>
            <button class="btn btn-primary" data-approve="${r.id}" style="padding:6px 12px">قبول</button>
            <button class="btn btn-danger" data-reject="${r.id}" style="padding:6px 12px">رفض</button>
          </div>`).join("") : `<div class="empty-hint">لا توجد طلبات معلّقة</div>`}
      </div>
    </div>`;
  root.querySelectorAll("[data-approve]").forEach((btn) => btn.addEventListener("click", () => withLoading(btn, async () => {
    await Api.patch(`/groups/${App.currentGroupId}/join-requests/${btn.dataset.approve}/approve`, {});
    toast("تم القبول"); renderRoute();
  })));
  root.querySelectorAll("[data-reject]").forEach((btn) => btn.addEventListener("click", () => withLoading(btn, async () => {
    await Api.patch(`/groups/${App.currentGroupId}/join-requests/${btn.dataset.reject}/reject`, {});
    toast("تم الرفض"); renderRoute();
  })));
});

/* ---- admin/audit ---- */

const AUDIT_LABELS = {
  member_added: "أضاف عضوًا", member_suspended: "علّق عضوًا", member_reactivated: "أعاد تفعيل عضو",
  member_deleted: "حذف عضوًا", members_reordered: "أعاد ترتيب الأعضاء", rent_updated: "غيّر الإيجار",
  expense_added: "أضاف مصروفًا", expense_updated: "عدّل مصروفًا", expense_deleted: "حذف مصروفًا",
  period_opened: "بدأ شهرًا جديدًا", period_closed: "أغلق الشهر", settings_updated: "غيّر الإعدادات",
  join_request_approved: "قبل طلب انضمام", join_request_rejected: "رفض طلب انضمام", member_joined_direct: "انضم للمجموعة"
};

screen("admin/audit", async (root) => {
  const data = await Api.get(`/groups/${App.currentGroupId}/audit-log`);
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">سجل العمليات</div>
      <div class="card-list">
        ${data.entries.length ? data.entries.map((e) => `
          <div class="expense-item">
            <div class="icon">📜</div>
            <div class="info">
              <div class="name">${escapeHtml(e.actor_name || "—")} ${AUDIT_LABELS[e.action] || e.action}</div>
              <div class="meta">${timeAgo(e.created_at)}</div>
            </div>
          </div>`).join("") : `<div class="empty-hint">لا يوجد سجل بعد</div>`}
      </div>
    </div>`;
});

/* ---- admin/settings ---- */

screen("admin/settings", async (root) => {
  const group = (await Api.get(`/groups/${App.currentGroupId}`)).group;
  root.innerHTML = `
    <div class="screen">
      <div class="section-title">إعدادات المجموعة</div>
      <div class="field">
        <label>من يستطيع إضافة مصروف؟</label>
        <select id="permSelect">
          <option value="admin_only" ${group.expense_add_permission === "admin_only" ? "selected" : ""}>المشرف فقط</option>
          <option value="all_members" ${group.expense_add_permission === "all_members" ? "selected" : ""}>جميع الأعضاء</option>
        </select>
      </div>
      <div class="field">
        <label>الانضمام يحتاج موافقة؟</label>
        <select id="approvalSelect">
          <option value="1" ${group.join_requires_approval ? "selected" : ""}>نعم</option>
          <option value="0" ${!group.join_requires_approval ? "selected" : ""}>لا، انضمام مباشر</option>
        </select>
      </div>
      <div class="field"><label>كود الدعوة</label><input value="${group.invite_code}" disabled dir="ltr"></div>
      <button class="btn btn-primary btn-block" id="saveSettingsBtn">حفظ</button>
    </div>`;
  const saveSettingsBtn = document.getElementById("saveSettingsBtn");
  saveSettingsBtn.addEventListener("click", () => withLoading(saveSettingsBtn, async () => {
    await Api.patch(`/groups/${App.currentGroupId}/settings`, {
      expenseAddPermission: document.getElementById("permSelect").value,
      joinRequiresApproval: document.getElementById("approvalSelect").value === "1"
    });
    toast("تم الحفظ"); location.hash = "#/admin";
  }));
});

/* ================= boot ================= */
// The free hosting tier sleeps after inactivity and can take 30-50s to wake on
// the very first request — without an explanation that reads as "broken", not
// "loading". If boot takes more than ~2.5s, we surface that explicitly.

function hideBootSplash() {
  const el = document.getElementById("bootSplash");
  if (!el) return;
  el.style.opacity = "0";
  setTimeout(() => el.remove(), 250);
}

(async function boot() {
  updateOfflineBanner();
  const wakeTimer = setTimeout(() => {
    const msg = document.getElementById("wakeMsg");
    if (msg) msg.classList.add("show");
  }, 2500);

  const me = Api.getMe();
  const { accessToken } = Api.getTokens();
  if (me && accessToken) {
    App.me = me;
    try {
      await afterLogin();
      clearTimeout(wakeTimer);
      hideBootSplash();
      if (!location.hash || location.hash === "#/login") {
        location.hash = App.groups.length ? "#/home" : "#/onboarding";
      } else {
        renderRoute();
      }
      return;
    } catch (e) {
      Api.clearTokens();
    }
  }
  clearTimeout(wakeTimer);
  hideBootSplash();
  renderRoute();
})();

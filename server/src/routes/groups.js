const express = require("express");
const crypto = require("crypto");
const db = require("../db");
const { requireAuth, loadMembership, requireRole, requireActiveMembership } = require("../auth/middleware");
const {
  computeGroupSettlement,
  getAllMemberships,
  getOpenPeriod,
  getGroupOr404
} = require("../services/settlement-service");
const { emitToGroup, notifyUsers, writeAudit } = require("../services/events");
const { fmtMoney, statusOf, toFils } = require("../financial-engine");
const asyncHandler = require("../utils/async-handler");

const router = express.Router();

function monthLabel(date = new Date()) {
  return date.toLocaleDateString("ar-KW-u-nu-latn", { year: "numeric", month: "long" });
}

function generateInviteCode() {
  return crypto.randomBytes(3).toString("hex").toUpperCase();
}

function serializeMembership(m, settlement) {
  const stats = settlement && settlement.byUserId[m.user_id];
  return {
    membershipId: m.id,
    userId: m.user_id,
    name: m.user_name,
    phone: m.user_phone,
    role: m.role,
    status: m.status,
    sortOrder: m.sort_order,
    ...(stats
      ? {
          rentShareFils: stats.rentShareFils,
          expenseShareFils: stats.expenseShareFils,
          paidFils: stats.paidFils,
          fairShareFils: stats.fairShareFils,
          dueFils: stats.dueFils,
          status_financial: statusOf(stats.dueFils)
        }
      : { status_financial: null })
  };
}

// ---- Group lifecycle ----

router.get("/mine", requireAuth, asyncHandler(async (req, res) => {
  const rows = await db
    .prepare(
      `SELECT g.*, m.role, m.status as membership_status FROM groups g
       JOIN memberships m ON m.group_id = g.id
       WHERE m.user_id = ? ORDER BY g.created_at ASC`
    )
    .all(req.user.id);
  res.json({ groups: rows });
}));

router.post("/", requireAuth, asyncHandler(async (req, res) => {
  const name = String(req.body.name || "").trim();
  if (!name) return res.status(400).json({ error: "اسم المجموعة مطلوب" });

  const groupId = crypto.randomUUID();
  let inviteCode = generateInviteCode();
  while (await db.prepare("SELECT 1 FROM groups WHERE invite_code = ?").get(inviteCode)) {
    inviteCode = generateInviteCode();
  }
  await db.prepare(
    "INSERT INTO groups (id, name, invite_code, admin_user_id, rent_fils) VALUES (?, ?, ?, ?, 0)"
  ).run(groupId, name, inviteCode, req.user.id);

  await db.prepare(
    "INSERT INTO memberships (id, group_id, user_id, role, status, sort_order) VALUES (?, ?, ?, 'admin', 'active', 0)"
  ).run(crypto.randomUUID(), groupId, req.user.id);

  await db.prepare(
    "INSERT INTO financial_periods (id, group_id, label, rent_fils_snapshot) VALUES (?, ?, ?, 0)"
  ).run(crypto.randomUUID(), groupId, monthLabel());

  res.status(201).json({ group: await getGroupOr404(groupId) });
}));

router.post("/join", requireAuth, asyncHandler(async (req, res) => {
  const inviteCode = String(req.body.inviteCode || "").trim().toUpperCase();
  const group = await db.prepare("SELECT * FROM groups WHERE invite_code = ?").get(inviteCode);
  if (!group) return res.status(404).json({ error: "كود الدعوة غير صحيح" });

  const existing = await db.prepare("SELECT * FROM memberships WHERE group_id = ? AND user_id = ?").get(group.id, req.user.id);
  if (existing) return res.status(400).json({ error: "أنت عضو بالفعل في هذه المجموعة" });

  if (!group.join_requires_approval) {
    const maxRow = await db
      .prepare("SELECT COALESCE(MAX(sort_order),-1) as m FROM memberships WHERE group_id = ?")
      .get(group.id);
    const membershipId = crypto.randomUUID();
    await db.prepare(
      "INSERT INTO memberships (id, group_id, user_id, role, status, sort_order) VALUES (?, ?, ?, 'member', 'active', ?)"
    ).run(membershipId, group.id, req.user.id, maxRow.m + 1);
    await writeAudit(group.id, req.user.id, "member_joined_direct", "membership", membershipId, null, { userId: req.user.id });
    await notifyUsers(group.id, [group.admin_user_id], {
      type: "member_added",
      title: "عضو جديد",
      message: `${req.user.name || req.user.phone} انضم إلى المجموعة`,
      relatedEntityType: "membership",
      relatedEntityId: membershipId
    });
    emitToGroup(group.id, "MemberAdded", { membershipId });
    return res.json({ status: "joined", groupId: group.id });
  }

  const existingReq = await db.prepare("SELECT * FROM join_requests WHERE group_id = ? AND user_id = ?").get(group.id, req.user.id);
  if (existingReq) {
    await db.prepare("UPDATE join_requests SET status='pending', created_at=now() WHERE id = ?").run(existingReq.id);
  } else {
    await db.prepare("INSERT INTO join_requests (id, group_id, user_id, status) VALUES (?, ?, ?, 'pending')").run(
      crypto.randomUUID(),
      group.id,
      req.user.id
    );
  }
  await notifyUsers(group.id, [group.admin_user_id], {
    type: "join_request",
    title: "عضو جديد يريد الانضمام",
    message: `${req.user.name || req.user.phone} طلب الانضمام`,
    priority: "important"
  });
  res.json({ status: "pending" });
}));

// Everything below this line concerns a specific group and requires membership.
router.use("/:groupId", requireAuth, loadMembership);

router.get("/:groupId", asyncHandler(async (req, res) => {
  res.json({ group: await getGroupOr404(req.params.groupId), membership: req.membership });
}));

router.delete("/:groupId", requireRole("admin"), asyncHandler(async (req, res) => {
  const groupId = req.params.groupId;
  await db.prepare("DELETE FROM expenses WHERE period_id IN (SELECT id FROM financial_periods WHERE group_id = ?)").run(groupId);
  await db.prepare("DELETE FROM financial_periods WHERE group_id = ?").run(groupId);
  await db.prepare("DELETE FROM notifications WHERE group_id = ?").run(groupId);
  await db.prepare("DELETE FROM audit_log WHERE group_id = ?").run(groupId);
  await db.prepare("DELETE FROM join_requests WHERE group_id = ?").run(groupId);
  await db.prepare("DELETE FROM memberships WHERE group_id = ?").run(groupId);
  await db.prepare("DELETE FROM groups WHERE id = ?").run(groupId);
  emitToGroup(groupId, "GroupDeleted", { groupId });
  res.json({ ok: true });
}));

router.get("/:groupId/dashboard", requireActiveMembership, asyncHandler(async (req, res) => {
  const { group, period, activeMembers, expenses, settlement } = await computeGroupSettlement(req.params.groupId);
  const me = settlement ? settlement.byUserId[req.user.id] : null;
  res.json({
    group,
    period,
    me: me
      ? { ...me, status: statusOf(me.dueFils) }
      : null,
    membersCount: activeMembers.length,
    totalExpensesFils: settlement ? settlement.totalExpensesFils : 0,
    recentExpenses: expenses.slice(0, 5),
    myRole: req.membership.role
  });
}));

router.get("/:groupId/members", asyncHandler(async (req, res) => {
  const { settlement } = await computeGroupSettlement(req.params.groupId);
  const all = await getAllMemberships(req.params.groupId);
  const list = req.membership.role === "admin" ? all : all.filter((m) => m.status === "active");
  res.json({ members: list.map((m) => serializeMembership(m, settlement)) });
}));

router.get("/:groupId/members/:membershipId/detail", asyncHandler(async (req, res) => {
  const { settlement, expenses } = await computeGroupSettlement(req.params.groupId);
  const m = await db.prepare("SELECT * FROM memberships WHERE id = ? AND group_id = ?").get(req.params.membershipId, req.params.groupId);
  if (!m) return res.status(404).json({ error: "العضو غير موجود" });
  const stats = settlement && settlement.byUserId[m.user_id];
  const myExpenses = expenses.filter((e) => e.paid_by_user_id === m.user_id);
  res.json({
    membershipId: m.id,
    userId: m.user_id,
    status: m.status,
    stats: stats
      ? { ...stats, statusWord: statusOf(stats.dueFils) }
      : null,
    expenses: myExpenses.map((e) => ({
      id: e.id,
      name: e.name,
      amountFils: e.amount_fils,
      category: e.category,
      createdAt: e.created_at
    }))
  });
}));

// ---- Admin: member management ----

router.post("/:groupId/members", requireRole("admin"), asyncHandler(async (req, res) => {
  const phone = String(req.body.phone || "").replace(/[^\d+]/g, "");
  const name = String(req.body.name || "").trim();
  if (!/^\+?\d{8,15}$/.test(phone)) return res.status(400).json({ error: "رقم هاتف غير صالح" });

  let user = await db.prepare("SELECT * FROM users WHERE phone = ?").get(phone);
  if (!user) {
    const id = crypto.randomUUID();
    await db.prepare("INSERT INTO users (id, phone, name) VALUES (?, ?, ?)").run(id, phone, name);
    user = await db.prepare("SELECT * FROM users WHERE id = ?").get(id);
  } else if (name && !user.name) {
    await db.prepare("UPDATE users SET name = ? WHERE id = ?").run(name, user.id);
  }

  const existing = await db.prepare("SELECT * FROM memberships WHERE group_id = ? AND user_id = ?").get(req.params.groupId, user.id);
  if (existing) return res.status(400).json({ error: "هذا الشخص عضو بالفعل" });

  const maxRow = await db
    .prepare("SELECT COALESCE(MAX(sort_order),-1) as m FROM memberships WHERE group_id = ?")
    .get(req.params.groupId);
  const membershipId = crypto.randomUUID();
  await db.prepare(
    "INSERT INTO memberships (id, group_id, user_id, role, status, sort_order) VALUES (?, ?, ?, 'member', 'active', ?)"
  ).run(membershipId, req.params.groupId, user.id, maxRow.m + 1);

  await writeAudit(req.params.groupId, req.user.id, "member_added", "membership", membershipId, null, { phone, name });
  const allM = await getAllMemberships(req.params.groupId);
  const activeAdmins = allM.filter((m) => m.status === "active").map((m) => m.user_id);
  await notifyUsers(req.params.groupId, activeAdmins, {
    type: "member_added",
    title: "عضو جديد",
    message: `تمت إضافة ${name || phone} إلى المجموعة`,
    relatedEntityType: "membership",
    relatedEntityId: membershipId
  });
  emitToGroup(req.params.groupId, "MemberAdded", { membershipId });
  res.status(201).json({ membershipId });
}));

router.patch("/:groupId/members/:membershipId/suspend", requireRole("admin"), asyncHandler(async (req, res) => {
  const m = await db.prepare("SELECT * FROM memberships WHERE id = ? AND group_id = ?").get(req.params.membershipId, req.params.groupId);
  if (!m) return res.status(404).json({ error: "العضو غير موجود" });
  if (m.user_id === req.user.id) return res.status(400).json({ error: "لا يمكنك تعليق نفسك" });

  await db.prepare("UPDATE memberships SET status = 'suspended' WHERE id = ?").run(m.id);
  await writeAudit(req.params.groupId, req.user.id, "member_suspended", "membership", m.id, { status: "active" }, { status: "suspended" });
  await notifyUsers(req.params.groupId, [m.user_id], {
    type: "member_suspended",
    title: "تم تعليق مشاركتك",
    message: "تم تعليق مشاركتك في المجموعة من قبل المشرف",
    priority: "important"
  });
  emitToGroup(req.params.groupId, "MemberSuspended", { membershipId: m.id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true });
}));

router.patch("/:groupId/members/:membershipId/reactivate", requireRole("admin"), asyncHandler(async (req, res) => {
  const m = await db.prepare("SELECT * FROM memberships WHERE id = ? AND group_id = ?").get(req.params.membershipId, req.params.groupId);
  if (!m) return res.status(404).json({ error: "العضو غير موجود" });

  await db.prepare("UPDATE memberships SET status = 'active' WHERE id = ?").run(m.id);
  await writeAudit(req.params.groupId, req.user.id, "member_reactivated", "membership", m.id, { status: "suspended" }, { status: "active" });
  await notifyUsers(req.params.groupId, [m.user_id], {
    type: "member_reactivated",
    title: "أصبحت عضوًا نشطًا",
    message: "أعاد المشرف تفعيل مشاركتك في المجموعة"
  });
  emitToGroup(req.params.groupId, "MemberReactivated", { membershipId: m.id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true });
}));

router.delete("/:groupId/members/:membershipId", requireRole("admin"), asyncHandler(async (req, res) => {
  const m = await db.prepare("SELECT * FROM memberships WHERE id = ? AND group_id = ?").get(req.params.membershipId, req.params.groupId);
  if (!m) return res.status(404).json({ error: "العضو غير موجود" });
  if (m.user_id === req.user.id) return res.status(400).json({ error: "لا يمكنك حذف نفسك" });

  await db.prepare("DELETE FROM memberships WHERE id = ?").run(m.id);
  await writeAudit(req.params.groupId, req.user.id, "member_deleted", "membership", m.id, { userId: m.user_id }, null);
  emitToGroup(req.params.groupId, "MemberDeleted", { membershipId: m.id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true });
}));

router.patch("/:groupId/members/reorder", requireRole("admin"), asyncHandler(async (req, res) => {
  const { orderedMembershipIds } = req.body;
  if (!Array.isArray(orderedMembershipIds)) return res.status(400).json({ error: "orderedMembershipIds مطلوب" });
  const update = db.prepare("UPDATE memberships SET sort_order = ? WHERE id = ? AND group_id = ?");
  for (let index = 0; index < orderedMembershipIds.length; index++) {
    await update.run(index, orderedMembershipIds[index], req.params.groupId);
  }
  await writeAudit(req.params.groupId, req.user.id, "members_reordered", "group", req.params.groupId, null, { orderedMembershipIds });
  emitToGroup(req.params.groupId, "MembersReordered", { orderedMembershipIds });
  res.json({ ok: true });
}));

// ---- Admin: rent ----

router.patch("/:groupId/rent", requireRole("admin"), asyncHandler(async (req, res) => {
  const rentFils = toFils(req.body.rent);
  if (!(rentFils >= 0)) return res.status(400).json({ error: "قيمة إيجار غير صالحة" });

  const group = await getGroupOr404(req.params.groupId);
  const oldRent = group.rent_fils;
  await db.prepare("UPDATE groups SET rent_fils = ? WHERE id = ?").run(rentFils, req.params.groupId);
  const period = await getOpenPeriod(req.params.groupId);
  if (period) {
    await db.prepare("UPDATE financial_periods SET rent_fils_snapshot = ? WHERE id = ?").run(rentFils, period.id);
  }
  await writeAudit(req.params.groupId, req.user.id, "rent_updated", "group", req.params.groupId, { rentFils: oldRent }, { rentFils });

  const allM = await getAllMemberships(req.params.groupId);
  const activeUserIds = allM.filter((m) => m.status === "active").map((m) => m.user_id);
  await notifyUsers(req.params.groupId, activeUserIds, {
    type: "rent_updated",
    title: "تم تحديث الإيجار الشهري",
    message: `تغيّر الإيجار من ${fmtMoney(oldRent)} إلى ${fmtMoney(rentFils)}، وتم تحديث الحسابات`,
    priority: "important"
  });
  emitToGroup(req.params.groupId, "RentUpdated", { rentFils });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true, rentFils });
}));

// ---- Admin: join requests ----

router.get("/:groupId/join-requests", requireRole("admin"), asyncHandler(async (req, res) => {
  const rows = await db
    .prepare(
      `SELECT jr.*, u.name, u.phone FROM join_requests jr JOIN users u ON u.id = jr.user_id
       WHERE jr.group_id = ? AND jr.status = 'pending' ORDER BY jr.created_at ASC`
    )
    .all(req.params.groupId);
  res.json({ requests: rows });
}));

router.patch("/:groupId/join-requests/:requestId/approve", requireRole("admin"), asyncHandler(async (req, res) => {
  const jr = await db.prepare("SELECT * FROM join_requests WHERE id = ? AND group_id = ?").get(req.params.requestId, req.params.groupId);
  if (!jr || jr.status !== "pending") return res.status(404).json({ error: "الطلب غير موجود" });

  const maxRow = await db
    .prepare("SELECT COALESCE(MAX(sort_order),-1) as m FROM memberships WHERE group_id = ?")
    .get(req.params.groupId);
  const membershipId = crypto.randomUUID();
  await db.prepare(
    "INSERT INTO memberships (id, group_id, user_id, role, status, sort_order) VALUES (?, ?, ?, 'member', 'active', ?)"
  ).run(membershipId, req.params.groupId, jr.user_id, maxRow.m + 1);
  await db.prepare("UPDATE join_requests SET status = 'approved' WHERE id = ?").run(jr.id);

  await writeAudit(req.params.groupId, req.user.id, "join_request_approved", "join_request", jr.id, null, { userId: jr.user_id });
  await notifyUsers(req.params.groupId, [jr.user_id], { type: "join_approved", title: "تم قبول طلبك", message: "تمت الموافقة على انضمامك للمجموعة" });
  emitToGroup(req.params.groupId, "MemberAdded", { membershipId });
  res.json({ ok: true });
}));

router.patch("/:groupId/join-requests/:requestId/reject", requireRole("admin"), asyncHandler(async (req, res) => {
  const jr = await db.prepare("SELECT * FROM join_requests WHERE id = ? AND group_id = ?").get(req.params.requestId, req.params.groupId);
  if (!jr || jr.status !== "pending") return res.status(404).json({ error: "الطلب غير موجود" });
  await db.prepare("UPDATE join_requests SET status = 'rejected' WHERE id = ?").run(jr.id);
  await writeAudit(req.params.groupId, req.user.id, "join_request_rejected", "join_request", jr.id, null, null);
  await notifyUsers(req.params.groupId, [jr.user_id], { type: "join_rejected", title: "تم رفض طلبك", message: "لم تتم الموافقة على طلب انضمامك" });
  res.json({ ok: true });
}));

// ---- Admin: audit log ----

router.get("/:groupId/audit-log", requireRole("admin"), asyncHandler(async (req, res) => {
  const rows = await db
    .prepare(
      `SELECT a.*, u.name as actor_name FROM audit_log a JOIN users u ON u.id = a.actor_user_id
       WHERE a.group_id = ? ORDER BY a.created_at DESC LIMIT 200`
    )
    .all(req.params.groupId);
  res.json({ entries: rows });
}));

// ---- Admin: settings ----

router.patch("/:groupId/settings", requireRole("admin"), asyncHandler(async (req, res) => {
  const fields = [];
  const values = [];
  if (typeof req.body.expenseAddPermission === "string") {
    fields.push("expense_add_permission = ?");
    values.push(req.body.expenseAddPermission);
  }
  if (typeof req.body.joinRequiresApproval === "boolean") {
    fields.push("join_requires_approval = ?");
    values.push(req.body.joinRequiresApproval ? 1 : 0);
  }
  if (typeof req.body.settlementDay === "number") {
    fields.push("settlement_day = ?");
    values.push(req.body.settlementDay);
  }
  if (!fields.length) return res.status(400).json({ error: "لا يوجد تغييرات" });
  values.push(req.params.groupId);
  await db.prepare(`UPDATE groups SET ${fields.join(", ")} WHERE id = ?`).run(...values);
  await writeAudit(req.params.groupId, req.user.id, "settings_updated", "group", req.params.groupId, null, req.body);
  res.json({ ok: true, group: await getGroupOr404(req.params.groupId) });
}));

module.exports = router;

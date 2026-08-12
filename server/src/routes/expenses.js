const express = require("express");
const crypto = require("crypto");
const db = require("../db");
const { requireAuth, loadMembership, requireActiveMembership } = require("../auth/middleware");
const { getOpenPeriod, getAllMemberships, getGroupOr404 } = require("../services/settlement-service");
const { emitToGroup, notifyUsers, writeAudit } = require("../services/events");
const { toFils, fmtMoney } = require("../financial-engine");
const asyncHandler = require("../utils/async-handler");

const router = express.Router({ mergeParams: true });

function canAddExpense(group, membership) {
  if (membership.role === "admin") return true;
  return group.expense_add_permission === "all_members";
}

async function activeUserIds(groupId) {
  const all = await getAllMemberships(groupId);
  return all.filter((m) => m.status === "active").map((m) => m.user_id);
}

router.use(requireAuth, loadMembership, requireActiveMembership);

router.get("/", asyncHandler(async (req, res) => {
  const period = await getOpenPeriod(req.params.groupId);
  if (!period) return res.json({ expenses: [], nextCursor: null });

  const limit = Math.min(50, Number(req.query.limit) || 20);
  const cursor = req.query.cursor;
  const rows = cursor
    ? await db
        .prepare(
          `SELECT e.*, u.name as paid_by_name, c.name as created_by_name FROM expenses e
           JOIN users u ON u.id = e.paid_by_user_id JOIN users c ON c.id = e.created_by_user_id
           WHERE e.period_id = ? AND e.created_at < ?::timestamptz ORDER BY e.created_at DESC LIMIT ?`
        )
        .all(period.id, cursor, limit + 1)
    : await db
        .prepare(
          `SELECT e.*, u.name as paid_by_name, c.name as created_by_name FROM expenses e
           JOIN users u ON u.id = e.paid_by_user_id JOIN users c ON c.id = e.created_by_user_id
           WHERE e.period_id = ? ORDER BY e.created_at DESC LIMIT ?`
        )
        .all(period.id, limit + 1);

  const hasMore = rows.length > limit;
  const page = rows.slice(0, limit);
  res.json({
    expenses: page.map((e) => ({
      id: e.id,
      name: e.name,
      amountFils: e.amount_fils,
      category: e.category,
      paidByUserId: e.paid_by_user_id,
      paidByName: e.paid_by_name,
      createdByName: e.created_by_name,
      notes: e.notes,
      createdAt: e.created_at
    })),
    nextCursor: hasMore ? page[page.length - 1].created_at : null
  });
}));

router.post("/", asyncHandler(async (req, res) => {
  const group = await getGroupOr404(req.params.groupId);
  if (!canAddExpense(group, req.membership)) {
    return res.status(403).json({ error: "لا تملك صلاحية إضافة مصروف — هذا مقصور على المشرف حسب إعدادات المجموعة" });
  }
  const period = await getOpenPeriod(req.params.groupId);
  if (!period) return res.status(400).json({ error: "لا يوجد شهر مالي مفتوح حاليًا" });

  const name = String(req.body.name || "").trim();
  const amountFils = toFils(req.body.amount);
  const category = String(req.body.category || "أخرى").trim() || "أخرى";
  const notes = req.body.notes ? String(req.body.notes).trim().slice(0, 500) : null;
  const paidByUserId = req.body.paidByUserId ? String(req.body.paidByUserId) : req.user.id;

  if (!name) return res.status(400).json({ error: "اسم المصروف مطلوب" });
  if (!(amountFils > 0)) return res.status(400).json({ error: "قيمة المصروف يجب أن تكون أكبر من صفر" });

  const payer = await db.prepare("SELECT * FROM memberships WHERE group_id = ? AND user_id = ?").get(req.params.groupId, paidByUserId);
  if (!payer) return res.status(400).json({ error: "الشخص الذي دفع يجب أن يكون عضوًا في المجموعة" });

  const id = crypto.randomUUID();
  await db.prepare(
    `INSERT INTO expenses (id, period_id, name, amount_fils, category, paid_by_user_id, created_by_user_id, notes)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, period.id, name, amountFils, category, paidByUserId, req.user.id, notes);

  await writeAudit(req.params.groupId, req.user.id, "expense_added", "expense", id, null, { name, amountFils, paidByUserId });

  const recipients = await activeUserIds(req.params.groupId);
  await notifyUsers(req.params.groupId, recipients, {
    type: "expense_added",
    title: "مصروف جديد",
    message: `تمت إضافة مصروف "${name}" بقيمة ${fmtMoney(amountFils)} بواسطة ${req.user.name || req.user.phone}`,
    relatedEntityType: "expense",
    relatedEntityId: id
  });
  emitToGroup(req.params.groupId, "ExpenseAdded", { expenseId: id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.status(201).json({ id });
}));

function canModify(req, expense) {
  return req.membership.role === "admin" || expense.created_by_user_id === req.user.id;
}

router.patch("/:expenseId", asyncHandler(async (req, res) => {
  const expense = await db.prepare("SELECT * FROM expenses WHERE id = ? ").get(req.params.expenseId);
  if (!expense) return res.status(404).json({ error: "المصروف غير موجود" });
  if (!canModify(req, expense)) return res.status(403).json({ error: "لا تملك صلاحية تعديل هذا المصروف" });

  const oldAmount = expense.amount_fils;
  const newName = req.body.name !== undefined ? String(req.body.name).trim() : expense.name;
  const newAmount = req.body.amount !== undefined ? toFils(req.body.amount) : expense.amount_fils;
  const newCategory = req.body.category !== undefined ? String(req.body.category).trim() : expense.category;
  if (!(newAmount > 0)) return res.status(400).json({ error: "قيمة المصروف يجب أن تكون أكبر من صفر" });

  await db.prepare("UPDATE expenses SET name = ?, amount_fils = ?, category = ? WHERE id = ?").run(
    newName,
    newAmount,
    newCategory,
    expense.id
  );
  await writeAudit(req.params.groupId, req.user.id, "expense_updated", "expense", expense.id, { amountFils: oldAmount }, { amountFils: newAmount });

  const recipients = await activeUserIds(req.params.groupId);
  await notifyUsers(req.params.groupId, recipients, {
    type: "expense_updated",
    title: "تم تعديل مصروف",
    message: `المبلغ السابق ${fmtMoney(oldAmount)}، المبلغ الجديد ${fmtMoney(newAmount)}`,
    relatedEntityType: "expense",
    relatedEntityId: expense.id
  });
  emitToGroup(req.params.groupId, "ExpenseUpdated", { expenseId: expense.id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true });
}));

router.delete("/:expenseId", asyncHandler(async (req, res) => {
  const expense = await db.prepare("SELECT * FROM expenses WHERE id = ?").get(req.params.expenseId);
  if (!expense) return res.status(404).json({ error: "المصروف غير موجود" });
  if (!canModify(req, expense)) return res.status(403).json({ error: "لا تملك صلاحية حذف هذا المصروف" });

  await db.prepare("DELETE FROM expenses WHERE id = ?").run(expense.id);
  await writeAudit(req.params.groupId, req.user.id, "expense_deleted", "expense", expense.id, { amountFils: expense.amount_fils, name: expense.name }, null);

  const recipients = await activeUserIds(req.params.groupId);
  await notifyUsers(req.params.groupId, recipients, {
    type: "expense_deleted",
    title: "تم حذف مصروف",
    message: `تم حذف مصروف "${expense.name}" بقيمة ${fmtMoney(expense.amount_fils)} وتم تحديث الحسابات`
  });
  emitToGroup(req.params.groupId, "ExpenseDeleted", { expenseId: expense.id });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.json({ ok: true });
}));

module.exports = router;

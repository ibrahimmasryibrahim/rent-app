const express = require("express");
const db = require("../db");
const { requireAuth } = require("../auth/middleware");
const asyncHandler = require("../utils/async-handler");

const router = express.Router();

router.get("/", requireAuth, asyncHandler(async (req, res) => {
  const limit = Math.min(100, Number(req.query.limit) || 30);
  const filter = req.query.filter; // financial | expenses | members | admin | system | undefined(all)
  const filterMap = {
    financial: ["rent_updated", "balance_changed", "period_closed"],
    expenses: ["expense_added", "expense_updated", "expense_deleted"],
    members: ["member_added", "member_suspended", "member_reactivated", "member_deleted"],
    admin: ["join_request", "join_approved", "join_rejected"],
    system: ["new_month_started", "month_closed"]
  };
  let rows;
  if (filter && filterMap[filter]) {
    const types = filterMap[filter];
    const placeholders = types.map(() => "?").join(",");
    rows = await db
      .prepare(`SELECT * FROM notifications WHERE user_id = ? AND type IN (${placeholders}) ORDER BY created_at DESC LIMIT ?`)
      .all(req.user.id, ...types, limit);
  } else {
    rows = await db.prepare("SELECT * FROM notifications WHERE user_id = ? ORDER BY created_at DESC LIMIT ?").all(req.user.id, limit);
  }
  const countRow = await db
    .prepare("SELECT COUNT(*) as c FROM notifications WHERE user_id = ? AND read_at IS NULL")
    .get(req.user.id);
  res.json({ notifications: rows, unreadCount: Number(countRow.c) });
}));

router.patch("/read-all", requireAuth, asyncHandler(async (req, res) => {
  await db.prepare("UPDATE notifications SET read_at = now() WHERE user_id = ? AND read_at IS NULL").run(req.user.id);
  res.json({ ok: true });
}));

router.patch("/:id/read", requireAuth, asyncHandler(async (req, res) => {
  const row = await db.prepare("SELECT * FROM notifications WHERE id = ? AND user_id = ?").get(req.params.id, req.user.id);
  if (!row) return res.status(404).json({ error: "الإشعار غير موجود" });
  await db.prepare("UPDATE notifications SET read_at = now() WHERE id = ?").run(row.id);
  res.json({ ok: true });
}));

module.exports = router;

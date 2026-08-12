const express = require("express");
const crypto = require("crypto");
const bcrypt = require("bcryptjs");
const db = require("../db");
const { signAccessToken, issueRefreshToken, rotateRefreshToken } = require("../auth/jwt");
const { requireAuth } = require("../auth/middleware");
const asyncHandler = require("../utils/async-handler");

const router = express.Router();

function normalizePhone(phone) {
  return String(phone || "").replace(/[^\d+]/g, "");
}

// No verification code: identity is just "phone + name". The real gate is at
// the group level — joining a group still requires the group's admin to
// approve the join request (see /groups/join and /groups/:id/join-requests).
// Logging in never grants access to any group's data on its own; every group
// route re-checks membership.status server-side regardless of this endpoint.
router.post("/login", asyncHandler(async (req, res) => {
  const phone = normalizePhone(req.body.phone);
  const name = String(req.body.name || "").trim().slice(0, 60);
  if (!/^\+?\d{8,15}$/.test(phone)) {
    return res.status(400).json({ error: "رقم هاتف غير صالح" });
  }

  let user = await db.prepare("SELECT * FROM users WHERE phone = ?").get(phone);
  if (!user) {
    if (!name) return res.status(400).json({ error: "الاسم مطلوب لأول مرة" });
    const id = crypto.randomUUID();
    await db.prepare("INSERT INTO users (id, phone, name) VALUES (?, ?, ?)").run(id, phone, name);
    user = await db.prepare("SELECT * FROM users WHERE id = ?").get(id);
  } else if (name && !user.name) {
    await db.prepare("UPDATE users SET name = ? WHERE id = ?").run(name, user.id);
    user.name = name;
  }

  const accessToken = signAccessToken(user.id);
  const refreshToken = await issueRefreshToken(user.id, req.headers["user-agent"] || "");
  res.json({
    accessToken,
    refreshToken,
    user: { id: user.id, phone: user.phone, name: user.name }
  });
}));

router.post("/refresh", asyncHandler(async (req, res) => {
  const { refreshToken } = req.body;
  if (!refreshToken) return res.status(400).json({ error: "refreshToken مطلوب" });
  const rotated = await rotateRefreshToken(refreshToken, req.headers["user-agent"] || "");
  if (!rotated) return res.status(401).json({ error: "جلسة غير صالحة" });
  const accessToken = signAccessToken(rotated.userId);
  res.json({ accessToken, refreshToken: rotated.refreshToken });
}));

router.post("/logout", asyncHandler(async (req, res) => {
  const { refreshToken } = req.body;
  if (refreshToken) {
    const sessions = await db.prepare("SELECT * FROM sessions").all();
    const match = sessions.find((s) => bcrypt.compareSync(refreshToken, s.refresh_token_hash));
    if (match) await db.prepare("DELETE FROM sessions WHERE id = ?").run(match.id);
  }
  res.json({ ok: true });
}));

router.get("/me", requireAuth, (req, res) => {
  res.json({ id: req.user.id, phone: req.user.phone, name: req.user.name });
});

router.patch("/me", requireAuth, asyncHandler(async (req, res) => {
  const name = String(req.body.name || "").trim().slice(0, 60);
  await db.prepare("UPDATE users SET name = ? WHERE id = ?").run(name, req.user.id);
  res.json({ id: req.user.id, phone: req.user.phone, name });
}));

module.exports = router;

const crypto = require("crypto");
const jwt = require("jsonwebtoken");
const bcrypt = require("bcryptjs");
const db = require("../db");
const { getJwtSecret } = require("./secret");

const SECRET = getJwtSecret();
const ACCESS_TOKEN_TTL = "15m";
const REFRESH_TOKEN_TTL_DAYS = 30;

function signAccessToken(userId) {
  return jwt.sign({ sub: userId }, SECRET, { expiresIn: ACCESS_TOKEN_TTL });
}

function verifyAccessToken(token) {
  try {
    const payload = jwt.verify(token, SECRET);
    return payload.sub;
  } catch (e) {
    return null;
  }
}

async function issueRefreshToken(userId, deviceInfo) {
  const token = crypto.randomBytes(32).toString("hex");
  const tokenHash = bcrypt.hashSync(token, 10);
  const id = crypto.randomUUID();
  const expiresAt = new Date(Date.now() + REFRESH_TOKEN_TTL_DAYS * 24 * 60 * 60 * 1000);
  await db.prepare(
    "INSERT INTO sessions (id, user_id, refresh_token_hash, device_info, expires_at) VALUES (?, ?, ?, ?, ?)"
  ).run(id, userId, tokenHash, deviceInfo || null, expiresAt);
  return token;
}

async function rotateRefreshToken(oldToken, deviceInfo) {
  const sessions = await db.prepare("SELECT * FROM sessions WHERE expires_at > ?").all(new Date());
  const session = sessions.find((s) => bcrypt.compareSync(oldToken, s.refresh_token_hash));
  if (!session) return null;
  await db.prepare("DELETE FROM sessions WHERE id = ?").run(session.id);
  const newToken = await issueRefreshToken(session.user_id, deviceInfo);
  return { userId: session.user_id, refreshToken: newToken };
}

module.exports = { signAccessToken, verifyAccessToken, issueRefreshToken, rotateRefreshToken };

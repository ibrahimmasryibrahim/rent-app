const crypto = require("crypto");
const bcrypt = require("bcryptjs");
const db = require("../db");
const { sendOtp } = require("./sms-provider");

const OTP_TTL_MINUTES = 5;
const OTP_RESEND_COOLDOWN_SECONDS = 30;

function generateCode() {
  return String(crypto.randomInt(100000, 999999));
}

async function requestOtp(phone) {
  const recent = await db
    .prepare("SELECT created_at FROM otp_codes WHERE phone = ? ORDER BY created_at DESC LIMIT 1")
    .get(phone);
  if (recent) {
    const ageSeconds = (Date.now() - new Date(recent.created_at).getTime()) / 1000;
    if (ageSeconds < OTP_RESEND_COOLDOWN_SECONDS) {
      const wait = Math.ceil(OTP_RESEND_COOLDOWN_SECONDS - ageSeconds);
      const err = new Error(`الرجاء الانتظار ${wait} ثانية قبل طلب رمز جديد`);
      err.status = 429;
      throw err;
    }
  }

  const code = generateCode();
  const codeHash = bcrypt.hashSync(code, 10);
  const expiresAt = new Date(Date.now() + OTP_TTL_MINUTES * 60 * 1000);
  const id = crypto.randomUUID();

  await db.prepare(
    "INSERT INTO otp_codes (id, phone, code_hash, expires_at) VALUES (?, ?, ?, ?)"
  ).run(id, phone, codeHash, expiresAt);

  const result = await sendOtp(phone, code);
  return { expiresAt: expiresAt.toISOString(), ...(result.devCode ? { devCode: result.devCode } : {}) };
}

async function verifyOtp(phone, code) {
  const row = await db
    .prepare(
      "SELECT * FROM otp_codes WHERE phone = ? AND consumed_at IS NULL ORDER BY created_at DESC LIMIT 1"
    )
    .get(phone);
  if (!row) return { ok: false, reason: "لا يوجد رمز مُرسل لهذا الرقم" };
  if (new Date(row.expires_at).getTime() < Date.now()) {
    return { ok: false, reason: "انتهت صلاحية الرمز، اطلب رمزًا جديدًا" };
  }
  if (!bcrypt.compareSync(code, row.code_hash)) {
    return { ok: false, reason: "رمز غير صحيح" };
  }
  await db.prepare("UPDATE otp_codes SET consumed_at = ? WHERE id = ?").run(new Date(), row.id);
  return { ok: true };
}

module.exports = { requestOtp, verifyOtp };

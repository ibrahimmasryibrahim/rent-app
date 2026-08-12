// Pluggable SMS delivery. Today only a "dev" provider exists: it never talks to
// a real carrier, it logs the OTP to the server console and returns it in the
// API response so the flow is genuinely testable without a paid SMS account.
//
// To go live: implement a `send(phone, code)` that calls a real provider
// (e.g. Twilio) and set SMS_PROVIDER=twilio in the environment. Nothing else
// in the codebase needs to change — every caller only depends on this module's
// `sendOtp` function.

const PROVIDER = process.env.SMS_PROVIDER || "dev";

function sendOtpDev(phone, code) {
  console.log(`[DEV MODE][SMS] OTP for ${phone}: ${code} (not actually sent — no SMS provider configured)`);
  return { devCode: code };
}

async function sendOtp(phone, code) {
  if (PROVIDER === "dev") {
    return sendOtpDev(phone, code);
  }
  throw new Error(`SMS provider "${PROVIDER}" is not implemented yet. Set SMS_PROVIDER=dev or add an implementation.`);
}

module.exports = { sendOtp, PROVIDER };

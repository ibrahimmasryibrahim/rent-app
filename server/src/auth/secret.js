// Generates and persists a JWT signing secret on first run so restarts don't
// invalidate every session. Stored outside the repo's tracked files.
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const SECRET_PATH = path.join(__dirname, "..", "..", ".jwt-secret");

function getJwtSecret() {
  if (process.env.JWT_SECRET) return process.env.JWT_SECRET;
  if (fs.existsSync(SECRET_PATH)) {
    return fs.readFileSync(SECRET_PATH, "utf8").trim();
  }
  const secret = crypto.randomBytes(48).toString("hex");
  fs.writeFileSync(SECRET_PATH, secret, "utf8");
  return secret;
}

module.exports = { getJwtSecret };

// Express 4 does not forward rejected promises from async handlers to the
// error middleware on its own — without this, a failed await would just hang
// the request. Wrap every async route/middleware with this.
module.exports = function asyncHandler(fn) {
  return (req, res, next) => Promise.resolve(fn(req, res, next)).catch(next);
};

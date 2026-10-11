// Run every test in a timezone west of UTC so `new Date('yyyy-MM-dd')` bugs (which shift the
// date back a day) surface in tests rather than in users' browsers. See docs/DESIGN_DECISIONS.md.
export default function setup() {
  process.env.TZ = 'America/Los_Angeles'
}

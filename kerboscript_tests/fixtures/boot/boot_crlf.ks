// Boot-style fixture with CRLF line endings, as written by Windows editors.
// Keep the CRLF bytes: .gitattributes marks this file -text.
local bootlog is "0:/fixtures/boot.log".
if exists(bootlog) { deletepath(bootlog). }
print "boot: start".
runoncepath("0:/fixtures/lib/mathlib.ks", 2).
local cfg is lexicon("orbit", 80000, "mode",
  "auto").
print "boot: target " + scaled(cfg["orbit"]) + " " + cfg["mode"].
log "booted" to bootlog.
local message is "multi
line".
print message:length.
local n is 0.
until n >= 3 {
  set n to n + 1.
  wait 0.
}
print "boot: done after " + n + " waits, log " + open(bootlog):readall:string.

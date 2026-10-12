// Compatibility fixture: archive file I/O as flight scripts commonly use it.
// It runs on a disposable copy of the archive.
local dir is "0:/fixtures/work".
if exists(dir) { deletepath(dir). }
createdir(dir).
print exists(dir).

local f is create(dir + "/notes.txt").
f:writeln("line one").
f:write("line two").
f:writeln("").
f:writeln("line three").
print f:size.
print f:readall:string.
local it is f:readall:iterator.
until not it:next { print it:index + ": " + it:value. }

log "log entry 1" to dir + "/flight.log".
log "log entry 2" to dir + "/flight.log".
print open(dir + "/flight.log"):readall:string.

copypath(dir + "/notes.txt", dir + "/copy.txt").
movepath(dir + "/copy.txt", dir + "/moved.txt").
print exists(dir + "/copy.txt") + " " + exists(dir + "/moved.txt").
print open(dir + "/moved.txt"):readall:string = f:readall:string.

local state is lexicon("phase", "ascent", "targets", list(70000, 80000.5), "armed", true).
writejson(state, dir + "/state.json").
local back is readjson(dir + "/state.json").
print back["phase"] + " " + back["targets"][1] + " " + back["armed"].
print back:keys.

local raw is open(dir + "/notes.txt"):readraw.
local rawcopy is create(dir + "/raw.bin").
rawcopy:writeraw(raw).
print rawcopy:size = f:size.

runpath("0:/fixtures/lib/mathlib.ks", 3).
print scaled(5) + " " + clamp(12, 0, 10).
runoncepath("0:/fixtures/lib/mathlib.ks", 100).
print "loads " + mathlib_loads + ", scaled " + scaled(5).

local names is list().
for item in open(dir):list:values { names:add(item:name). }
print names:length.
deletepath(dir + "/moved.txt").
print exists(dir + "/moved.txt").
deletepath(dir).
print exists(dir).

// Compatibility fixture: WAIT, WAIT UNTIL, WHEN/THEN and ON triggers.
// Game time only advances in fixed updates, so the order is deterministic.
local ticks is 0.
local stage is 0.
when ticks >= 3 then {
  print "when fired at tick " + ticks.
}
when ticks >= 2 then {
  print "repeating trigger at tick " + ticks.
  if ticks < 6 { preserve. }
}
on stage {
  print "stage changed to " + stage.
  return true.
}
until ticks >= 7 {
  set ticks to ticks + 1.
  if mod(ticks, 3) = 0 { set stage to stage + 1. }
  wait 0.
}
print "loop done at tick " + ticks.

local flag is false.
when flag then { print "flag trigger". }
wait 0.1.
set flag to true.
wait until true.
wait 0.
print "after flag".

local samples is list().
from { local i is 0. } until i >= 3 step { set i to i + 1. } do {
  samples:add(i * i).
  wait 0.02.
}
print samples.

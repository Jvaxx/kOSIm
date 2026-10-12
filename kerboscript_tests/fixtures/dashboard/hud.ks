// Character-grid dashboard fixture, driven by keys from the test:
//   n  next frame       s  toggle a status word (shorter text overwrites)
//   w  wrap a long PRINT AT    p  plain PRINT lines (scrolls)
//   r  resize from the script   c  CLEARSCREEN   q  quit
// A resize watcher redraws the whole layout at the new size.
local frame is 0.
local armed is true.

function draw_labels {
  print "STATUS" at (0, 0).
  print "alt:" at (0, 2).
  print "spd:" at (0, 3).
  print "mode:" at (0, 4).
}
function draw_values {
  print ("" + frame * 1000):padleft(7) at (5, 2).
  print ("" + frame * 2.5):padleft(7) at (5, 3).
  print choose "ARMED" if armed else "OFF" at (6, 4).
  print "f" + frame + " " + terminal:width + "x" + terminal:height at (0, terminal:height - 1).
}
function redraw {
  clearscreen.
  draw_labels().
  draw_values().
}
terminal:resizewatchers:add({
  parameter cols, rows.
  redraw().
  return true.
}).

redraw().
until false {
  local key is terminal:input:getchar().
  if key = "q" { break. }
  if key = "n" { set frame to frame + 1. draw_values(). }
  if key = "s" { set armed to not armed. draw_values(). }
  if key = "w" { print "0123456789ABCDEFGHIJ" at (terminal:width - 6, 5). }
  if key = "p" { print "plain 1". print "plain 2". print "plain 3". }
  if key = "r" { set terminal:width to 20. set terminal:height to 6. }
  if key = "c" { clearscreen. print "cleared" at (2, 1). }
}
print "bye".

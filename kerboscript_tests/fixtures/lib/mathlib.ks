// Library used by fixtures through RUNPATH/RUNONCEPATH.
parameter scale is 1.
global mathlib_loads is choose mathlib_loads + 1 if defined mathlib_loads else 1.
global function scaled {
  parameter x.
  return x * scale.
}
global function clamp {
  parameter x, lo, hi.
  return max(lo, min(hi, x)).
}
print "mathlib loaded, scale " + scale.

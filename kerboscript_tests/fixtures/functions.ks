// Compatibility fixture: functions, parameters, recursion, closures and delegates.
function greet {
  parameter who, punct is "!".
  return "hello " + who + punct.
}
print greet("kerbin").
print greet("mun", "?").

function fact {
  parameter n.
  if n <= 1 { return 1. }
  return n * fact(n - 1).
}
print fact(10).

function counter {
  local n is 0.
  return { set n to n + 1. return n. }.
}
local c1 is counter().
local c2 is counter().
c1(). c1().
print c1() + " " + c2().

local add is { parameter a, b. return a + b. }.
print add(2, 3).
local add5 is add:bind(5).
print add5(10).
print add5:call(1).

local twice is { parameter f, x. return f(f(x)). }.
print twice(add5, 0).

local printer is print@.
printer("builtin delegate").
local power is {parameter b, e. return b ^ e.}.
print power:bind(2):call(8).

function apply_all {
  parameter fns, x.
  local out is list().
  for f in fns { out:add(f(x)). }
  return out.
}
print apply_all(list(add5, { parameter v. return v * v. }, fact@), 4).

// Optional arguments and returning from inside loops.
function first_over {
  parameter items, limit.
  for i in items { if i > limit { return i. } }
  return -1.
}
print first_over(list(1, 5, 9, 12), 6).
print first_over(list(1, 2), 6).
lock doubled to fact(3) * 2.
print doubled.

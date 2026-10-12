// Compatibility fixture: how PRINT shows each value type.
// The expected output lives in expected/printing.out. Recorded quirks, kept as-is:
// printing "" adds no line, and ROUND uses banker's rounding (round(2.5) = 2).
// Nested primitives print inline (FMT-01).
print 42.
print -7.
print 3.25.
print 1/3.
print 2^40.
print 1e-5.
print "plain string".
print "with ""quotes"" inside".
print "".
print true.
print false.
print 1 = 1.
print "a" + 1.
print 1 + "a".
print list().
print list(1, "two", true, 4.5).
print list(list(1, 2), list()).
print lexicon().
print lexicon("alt", 70000, "name", "Kerbin", "ok", false).
print lexicon(1, "one", 2.5, "two and a half").
print lexicon("inner", list(1, lexicon("k", "v"))).
print list(1, 2):dump.
print queue(1, 2).
print stack(1, 2).
print range(3).
print uniqueset(5, 5, 6).
set l to list(10, 20, 30).
print l[1] + l:length.
print l:sublist(1, 2).
print "abc":toupper + "DEF":tolower.
print round(2.5) + " " + round(3.14159, 2) + " " + floor(-0.5) + " " + mod(7, 3).

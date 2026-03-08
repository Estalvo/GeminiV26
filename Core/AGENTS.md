Gemini Confidence Rule:

FinalConfidence = 0.7 * EntryScore + 0.3 * LogicConfidence

FinalConfidence is immutable.
State penalties must never modify it.

All instruments must call:

ctx.ComputeFinalConfidence()

immediately after PositionContext creation.
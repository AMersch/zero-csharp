# lldb summaries for zero-mode C#: shows String, Span<char> and
# ReadOnlySpan<char> as text. Loaded by launch.json (initCommands), or by hand:
#   command script import .vscode/zero_lldb.py
import lldb

MAX_CHARS = 1024


def _read_utf16(process, address, length):
    if address == 0 or length < 0:
        return None
    shown = min(length, MAX_CHARS)
    if shown == 0:
        return '""'
    error = lldb.SBError()
    data = process.ReadMemory(address, shown * 2, error)
    if not error.Success():
        return None
    text = data.decode("utf-16-le", errors="replace")
    # Trailing zeros (an unfilled stackalloc buffer) as a count, not \0\0\0...
    trimmed = text.rstrip("\0")
    zeros = len(text) - len(trimmed)
    text = trimmed.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t").replace("\0", "\\0")
    text = '"' + text + ('..."' if shown < length else '"')
    if zeros:
        text += " + %d \\0" % zeros
    return text


def char_span_summary(valobj, internal_dict):
    value = valobj.GetNonSyntheticValue()
    length = value.GetChildMemberWithName("_length").GetValueAsSigned()
    reference = value.GetChildMemberWithName("_reference")
    address = reference.GetValueAsUnsigned(0)  # a char16_t&: its value is the address
    text = _read_utf16(valobj.GetProcess(), address, length)
    if text is None:
        return "length=%d" % length
    return "%s length=%d" % (text, length)


def string_summary(valobj, internal_dict):
    value = valobj.GetNonSyntheticValue()
    if value.TypeIsPointerType() or value.GetType().IsReferenceType():
        if value.GetValueAsUnsigned(0) == 0:
            return "null"
        value = value.Dereference()
    length = value.GetChildMemberWithName("_stringLength").GetValueAsSigned()
    first = value.GetChildMemberWithName("_firstChar")
    text = _read_utf16(valobj.GetProcess(), first.GetLoadAddress(), length)
    return text if text is not None else "<unreadable string>"


def __lldb_init_module(debugger, internal_dict):
    debugger.HandleCommand(
        'type summary add -x "^S_P_CoreLib_System_(ReadOnly)?Span_1<Char>$" '
        "-F zero_lldb.char_span_summary -w zero")
    debugger.HandleCommand('type summary add "String" -F zero_lldb.string_summary -w zero')
    debugger.HandleCommand("type category enable zero")
